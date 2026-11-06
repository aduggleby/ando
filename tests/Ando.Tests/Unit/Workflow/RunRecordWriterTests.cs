// =============================================================================
// RunRecordWriterTests.cs
//
// Summary: Tests for RunRecordWriter: file location, .gitignore, content and
// secret redaction. Uses the SecretValues collection because redaction reads
// the static secret set.
// =============================================================================

using System.Text.Json;
using Ando.Logging;
using Ando.Tests.TestFixtures;
using Ando.Tests.Unit.Logging;
using Ando.Workflow;

namespace Ando.Tests.Unit.Workflow;

[Trait("Category", "Unit")]
[Collection("SecretValues")]
public class RunRecordWriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ando-runrecord-").FullName;
    private readonly TestLogger _logger = new();
    private static readonly DateTimeOffset Started = new(2026, 11, 6, 12, 30, 45, TimeSpan.Zero);

    public RunRecordWriterTests() => SecretValues.Clear();

    public void Dispose()
    {
        SecretValues.Clear();
        Directory.Delete(_root, recursive: true);
    }

    private static WorkflowResult SampleResult() => new()
    {
        WorkflowName = "build",
        Success = false,
        WasCancelled = true,
        Duration = TimeSpan.FromSeconds(12.34),
        StepResults =
        [
            new StepResult { StepName = "Dotnet.Build", Context = "web", Success = true, Duration = TimeSpan.FromSeconds(2) },
            new StepResult
            {
                StepName = "Azure.Cli", Context = "login s3cr3t-value", Success = false, Depth = 1, Cleanup = true,
                ErrorMessage = "failed with s3cr3t-value"
            }
        ]
    };

    private string Write(RunSession session, WorkflowResult? result = null)
    {
        var path = RunRecordWriter.TryWrite(_root, Path.Combine(_root, "scripts", "build.csando"), ["release", "eu"],
            session, result ?? SampleResult(), Started, "9.9.9", _logger);
        return path.ShouldNotBeNull();
    }

    [Fact]
    public void TryWrite_CreatesTimestampedFileAndGitignore()
    {
        var path = Write(new RunSession());

        path.ShouldBe(Path.Combine(_root, ".ando", "runs", "20261106T123045Z.json"));
        File.Exists(path).ShouldBeTrue();
        File.ReadAllText(Path.Combine(_root, ".ando", "runs", ".gitignore")).ShouldBe("*\n");
        _logger.InfoMessages.ShouldContain(m => m.Contains("Run record:"));
    }

    [Fact]
    public void TryWrite_IncludesRunMetadataStepsAndReports()
    {
        var session = new RunSession { Ref = "abc123" };
        session.Reports.Add(new("eu.healthz", "ok"));
        session.Reports.Add(new("version", "1.0"));

        using var doc = JsonDocument.Parse(File.ReadAllText(Write(session)));
        var root = doc.RootElement;

        root.GetProperty("andoVersion").GetString().ShouldBe("9.9.9");
        root.GetProperty("script").GetString().ShouldBe("scripts/build.csando");
        root.GetProperty("profiles").EnumerateArray().Select(p => p.GetString()).ShouldBe(["release", "eu"]);
        root.GetProperty("ref").GetString().ShouldBe("abc123");
        root.GetProperty("success").GetBoolean().ShouldBeFalse();
        root.GetProperty("cancelled").GetBoolean().ShouldBeTrue();
        root.GetProperty("durationSeconds").GetDouble().ShouldBe(12.3);

        var steps = root.GetProperty("steps").EnumerateArray().ToList();
        steps.Count.ShouldBe(2);
        steps[0].GetProperty("name").GetString().ShouldBe("Dotnet.Build");
        steps[0].GetProperty("context").GetString().ShouldBe("web");
        steps[0].GetProperty("success").GetBoolean().ShouldBeTrue();
        steps[1].GetProperty("depth").GetInt32().ShouldBe(1);
        steps[1].GetProperty("cleanup").GetBoolean().ShouldBeTrue();

        var reports = root.GetProperty("reports");
        reports.GetProperty("eu.healthz").GetString().ShouldBe("ok");
        reports.GetProperty("version").GetString().ShouldBe("1.0");
    }

    [Fact]
    public void TryWrite_DuplicateReportKeys_LastValueWins()
    {
        var session = new RunSession();
        session.Reports.Add(new("k", "first"));
        session.Reports.Add(new("k", "second"));

        using var doc = JsonDocument.Parse(File.ReadAllText(Write(session)));

        doc.RootElement.GetProperty("reports").GetProperty("k").GetString().ShouldBe("second");
    }

    [Fact]
    public void TryWrite_RedactsSecretsInStepsAndReports()
    {
        SecretValues.Add("s3cr3t-value");
        var session = new RunSession();
        session.Reports.Add(new("token", "value is s3cr3t-value"));

        var text = File.ReadAllText(Write(session));

        text.ShouldNotContain("s3cr3t-value");
        text.ShouldContain("value is [REDACTED]");
        text.ShouldContain("login [REDACTED]");
        text.ShouldContain("failed with [REDACTED]");
    }

    [Fact]
    public void TryWrite_ExistingGitignoreIsKept()
    {
        var runs = Path.Combine(_root, ".ando", "runs");
        Directory.CreateDirectory(runs);
        File.WriteAllText(Path.Combine(runs, ".gitignore"), "custom\n");

        Write(new RunSession());

        File.ReadAllText(Path.Combine(runs, ".gitignore")).ShouldBe("custom\n");
    }

    [Fact]
    public void TryWrite_Failure_ReturnsNullAndWarns()
    {
        var blocker = Path.Combine(_root, "file");
        File.WriteAllText(blocker, "x");

        var path = RunRecordWriter.TryWrite(blocker, Path.Combine(_root, "build.csando"), [], new RunSession(),
            SampleResult(), Started, "1.0.0", _logger);

        path.ShouldBeNull();
        _logger.WarningMessages.ShouldContain(m => m.Contains("Could not write the run record"));
    }
}
