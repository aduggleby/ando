// =============================================================================
// RunFlagTests.cs
//
// Summary: Tests for the option check of `ando run` (AndoCli.FindUnknownRunFlag
// and FindRunFlagError): known options pass, typos and conflicts are rejected.
// =============================================================================

using Ando.Cli;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
public class RunFlagTests
{
    [Theory]
    [InlineData("run", "-p", "publish", "--dind", "--read-env")]
    [InlineData("run", "--dind", "--read-env", "-p", "x")]
    [InlineData("-f", "x.csando", "run", "--dind")]
    [InlineData("run", "positional")]
    [InlineData("run", "--no-env")]
    [InlineData("run", "--dry-run", "--dry-run-output", "/tmp/tree.txt")]
    [InlineData("--no-env", "-p", "release")]
    public void FindUnknownRunFlag_AcceptedCommandLines_ReturnNull(params string[] args)
    {
        AndoCli.FindUnknownRunFlag(args).ShouldBeNull();
        AndoCli.FindRunFlagError(args).ShouldBeNull();
    }

    [Theory]
    [InlineData("--dry-rn", "run", "--dry-rn")]
    [InlineData("--help", "run", "--help")]
    [InlineData("-h", "run", "-h")]
    [InlineData("-v", "run", "-v")]
    [InlineData("--read_env", "run", "-p", "x", "--read_env")]
    public void FindUnknownRunFlag_RejectsUnknownOptions(string expected, params string[] args)
    {
        AndoCli.FindUnknownRunFlag(args).ShouldBe(expected);
        AndoCli.FindRunFlagError(args).ShouldBe($"Unknown option for run: {expected}");
    }

    [Theory]
    [InlineData("run", "--dry-run", "--dry-run-output")]
    [InlineData("run", "--dry-run", "--dry-run-output", "--yes")]
    public void FindRunFlagError_DryRunOutputWithoutAPath_IsAnError(params string[] args) =>
        AndoCli.FindRunFlagError(args).ShouldBe("--dry-run-output needs a file path.");

    [Fact]
    public void FindRunFlagError_NoEnvWithReadEnv_IsAnError()
    {
        AndoCli.FindUnknownRunFlag(["run", "--no-env", "--read-env"]).ShouldBeNull();
        AndoCli.FindRunFlagError(["run", "--no-env", "--read-env"])
            .ShouldBe("--no-env and --read-env cannot be used together.");
    }
}

[Trait("Category", "Unit")]
public class RecordPathFileTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ando-recordpath-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void RecordPathFile_TakesAValue_AndIsAccepted()
    {
        AndoCli.FindUnknownRunFlag(["run", "--record-path-file", "path.txt", "--dry-run"]).ShouldBeNull();
        AndoCli.FindRunFlagError(["run", "--record-path-file", "path.txt"]).ShouldBeNull();
    }

    [Theory]
    [InlineData("run", "--record-path-file")]
    [InlineData("run", "--record-path-file", "--dry-run")]
    public void RecordPathFile_WithoutValue_IsAnError(params string[] args)
    {
        AndoCli.FindRunFlagError(args).ShouldBe("--record-path-file needs a file path.");
    }

    [Fact]
    public void WriteRecordPathFile_WritesTheAbsolutePathAsOneLine()
    {
        var file = Path.Combine(_dir, "path.txt");
        var record = Path.Combine(_dir, ".ando", "runs", "x.json");

        AndoCli.WriteRecordPathFile(file, _dir, record, new Ando.Tests.TestFixtures.TestLogger());

        File.ReadAllText(file).ShouldBe(record + "\n");
    }

    [Fact]
    public void WriteRecordPathFile_RelativeFileIsRelativeToTheInvocationDirectory()
    {
        AndoCli.WriteRecordPathFile("rel.txt", _dir, "runs/x.json", new Ando.Tests.TestFixtures.TestLogger());

        File.ReadAllText(Path.Combine(_dir, "rel.txt")).ShouldBe(Path.Combine(_dir, "runs", "x.json") + "\n");
    }

    [Fact]
    public void DeleteStalePathFile_RemovesAnOldPath_AndIgnoresMissingOrUnset()
    {
        var file = Path.Combine(_dir, "path.txt");
        File.WriteAllText(file, "old\n");

        AndoCli.DeleteStalePathFile("path.txt", _dir);
        File.Exists(file).ShouldBeFalse();

        AndoCli.DeleteStalePathFile("path.txt", _dir);
        AndoCli.DeleteStalePathFile(null, _dir);
    }

    [Fact]
    public void WriteRecordPathFile_NoOptionCreatesNothing()
    {
        AndoCli.WriteRecordPathFile(null, _dir, "x.json", new Ando.Tests.TestFixtures.TestLogger());

        Directory.GetFiles(_dir).ShouldBeEmpty();
    }

    [Fact]
    public void WriteRecordPathFile_UnwritableFile_WarnsOnly()
    {
        var logger = new Ando.Tests.TestFixtures.TestLogger();

        AndoCli.WriteRecordPathFile("missing-dir/p.txt", _dir, "x.json", logger);

        logger.WarningMessages.ShouldContain(m => m.Contains("Could not write the record path"));
    }
}
