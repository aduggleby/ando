// =============================================================================
// GitTagPushRemoteTests.cs
//
// Summary: Git.Tag with a message resolved at run time, Git.PushTag,
// Git.RemoteTags and Git.ChangedFiles(Func), against real temporary
// repositories (a work repo and a bare "origin").
// =============================================================================

using System.Diagnostics;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class GitTagPushRemoteTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ando-gitpush-").FullName;
    private readonly string _repo;
    private readonly string _origin;
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();

    public GitTagPushRemoteTests()
    {
        _repo = Path.Combine(_root, "work");
        _origin = Path.Combine(_root, "origin.git");
        Directory.CreateDirectory(_repo);
        Directory.CreateDirectory(_origin);
        Git(_origin, "init", "-q", "--bare");
        Git(_repo, "init", "-q");
        Git(_repo, "remote", "add", "origin", _origin);
        File.WriteAllText(Path.Combine(_repo, "a.txt"), "a");
        Commit("first");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private GitOperations CreateGit() =>
        new(_registry, _logger, () => new MockExecutor(), new RunSession { SourceRoot = _repo });

    private void Commit(string message)
    {
        Git(_repo, "add", "-A");
        Git(_repo, "-c", "user.name=t", "-c", "user.email=t@example.com", "-c", "commit.gpgsign=false", "commit", "-q", "-m", message);
    }

    private static string Git(string dir, params string[] args)
    {
        var info = new ProcessStartInfo("git") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)} failed");
        return output.Trim();
    }

    private Task<bool> RunLast() => _registry.Steps[^1].Execute();

    // The tag step needs an identity for annotated tags.
    private void TagWithIdentity(string name, string message)
    {
        Git(_repo, "-c", "user.name=t", "-c", "user.email=t@example.com", "tag", "-a", name, "-m", message);
    }

    [Fact]
    public async Task Tag_WithMessageFunc_IsEvaluatedWhenTheStepRuns()
    {
        Git(_repo, "config", "user.name", "t");
        Git(_repo, "config", "user.email", "t@example.com");
        var hash = "early";
        CreateGit().Tag("v1.0.0", o => o.WithMessage(() => $"release\n\nsha: {hash}"));

        hash = "late-value";
        (await RunLast()).ShouldBeTrue();

        Git(_repo, "tag", "-l", "--format=%(contents)", "v1.0.0").ShouldContain("sha: late-value");
    }

    [Fact]
    public async Task Tag_WithMessageString_StillWorks()
    {
        Git(_repo, "config", "user.name", "t");
        Git(_repo, "config", "user.email", "t@example.com");
        CreateGit().Tag("v1.0.0", o => o.WithMessage("plain message"));

        (await RunLast()).ShouldBeTrue();

        Git(_repo, "tag", "-l", "--format=%(contents)", "v1.0.0").ShouldContain("plain message");
    }

    [Fact]
    public async Task PushTag_PushesOnlyThatTag()
    {
        TagWithIdentity("v1.0.0", "one");
        TagWithIdentity("v2.0.0", "two");
        CreateGit().PushTag("v1.0.0");

        (await RunLast()).ShouldBeTrue();

        _registry.Steps[0].Name.ShouldBe("Git.PushTag");
        _registry.Steps[0].Context.ShouldBe("v1.0.0");
        Git(_origin, "tag", "-l").ShouldBe("v1.0.0");
    }

    [Fact]
    public async Task PushTag_MissingLocalTag_FailsAndPushesNothing()
    {
        CreateGit().PushTag("v9.9.9");

        (await RunLast()).ShouldBeFalse();

        Git(_origin, "tag", "-l").ShouldBe("");
        _logger.ErrorMessages.ShouldContain(e => e.Contains("v9.9.9"));
    }

    [Fact]
    public async Task RemoteTags_ReturnsNamesWithoutRefPrefix_FilteredByPattern()
    {
        TagWithIdentity("v1.0.0", "one");
        TagWithIdentity("v1.1.0", "two");
        TagWithIdentity("other", "three");
        Git(_repo, "push", "-q", "origin", "--tags");
        var step = CreateGit().RemoteTags("v*");

        (await RunLast()).ShouldBeTrue();

        _registry.Steps[0].Name.ShouldBe("Git.RemoteTags");
        _registry.Steps[0].Context.ShouldBe("v*");
        step.Value.Split('\n').OrderBy(x => x).ShouldBe(["v1.0.0", "v1.1.0"]);
    }

    [Fact]
    public async Task RemoteTags_NoMatch_IsEmpty()
    {
        var step = CreateGit().RemoteTags("v*");

        (await RunLast()).ShouldBeTrue();

        step.Value.ShouldBe("");
    }

    [Fact]
    public void ParseRemoteTags_StripsShaAndPrefix()
    {
        GitOperations.ParseRemoteTags("abc123\trefs/tags/v1\ndef456\trefs/tags/v2\n").ShouldBe("v1\nv2");
    }

    [Fact]
    public async Task ChangedFiles_Func_ResolvesRefWhenTheStepRuns()
    {
        TagWithIdentity("v1.0.0", "one");
        File.WriteAllText(Path.Combine(_repo, "b.txt"), "b");
        Commit("second");
        var sinceRef = "does-not-exist-yet";
        var step = CreateGit().ChangedFiles(() => sinceRef, "b.txt");

        sinceRef = "v1.0.0";
        (await RunLast()).ShouldBeTrue();

        _registry.Steps[0].Name.ShouldBe("Git.ChangedFiles");
        step.Value.ShouldBe("b.txt");
    }
}
