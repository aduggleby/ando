// =============================================================================
// GitEvalTimeTests.cs
//
// Summary: Tests for the evaluation-time git helpers (Commit, ShortCommit,
// RequireClean, ChangedSince, LastTag) against a real temporary git repository.
// =============================================================================

using System.Diagnostics;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class GitEvalTimeTests : IDisposable
{
    private readonly string _repo = Directory.CreateTempSubdirectory("ando-git-").FullName;
    private readonly TestLogger _logger = new();

    public GitEvalTimeTests()
    {
        Git("init", "-q");
        Write("src/a.txt", "a");
        Write("docs/b.txt", "b");
        Commit("first");
        Git("tag", "v1.0.0");
        Write("docs/b.txt", "b2");
        Commit("second");
    }

    public void Dispose() => Directory.Delete(_repo, recursive: true);

    private GitOperations CreateGit(string? sourceRoot = null) =>
        new(new StepRegistry(), _logger, () => new MockExecutor(), new RunSession { SourceRoot = sourceRoot ?? _repo });

    private string Git(params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            WorkingDirectory = _repo,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        process.ExitCode.ShouldBe(0, $"git {string.Join(' ', args)} failed");
        return output.Trim();
    }

    private void Write(string path, string content)
    {
        var full = Path.Combine(_repo, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    private void Commit(string message)
    {
        Git("add", "-A");
        Git("-c", "user.name=Test", "-c", "user.email=test@example.com", "-c", "commit.gpgsign=false",
            "commit", "-q", "-m", message);
    }

    // ---- Commit / ShortCommit ----------------------------------------------

    [Fact]
    public void Commit_IsFullHeadSha()
    {
        var git = CreateGit();

        git.Commit.ShouldBe(Git("rev-parse", "HEAD"));
        git.Commit.Length.ShouldBe(40);
    }

    [Fact]
    public void ShortCommit_IsPrefixOfCommit()
    {
        var git = CreateGit();

        git.ShortCommit.Length.ShouldBeGreaterThanOrEqualTo(7);
        git.Commit.ShouldStartWith(git.ShortCommit);
    }

    [Fact]
    public void Commit_OutsideARepository_Throws()
    {
        var outside = Directory.CreateTempSubdirectory("ando-nogit-").FullName;
        try
        {
            Should.Throw<InvalidOperationException>(() => CreateGit(outside).Commit);
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    // ---- RequireClean -------------------------------------------------------

    [Fact]
    public void RequireClean_CleanTree_Passes()
    {
        CreateGit().RequireClean();

        _logger.InfoMessages.ShouldContain(m => m.Contains("working tree is clean"));
    }

    [Fact]
    public void RequireClean_ModifiedTrackedFile_Throws()
    {
        Write("src/a.txt", "changed");

        var ex = Should.Throw<InvalidOperationException>(() => CreateGit().RequireClean());

        ex.Message.ShouldContain("src/a.txt");
    }

    [Fact]
    public void RequireClean_UntrackedFile_Throws()
    {
        Write("new/file.txt", "x");

        var ex = Should.Throw<InvalidOperationException>(() => CreateGit().RequireClean());

        ex.Message.ShouldContain("new/file.txt");
    }

    [Fact]
    public void RequireClean_StagedAndDeletedChanges_Throw()
    {
        Write("src/staged.txt", "x");
        Git("add", "src/staged.txt");
        Should.Throw<InvalidOperationException>(() => CreateGit().RequireClean());

        Git("reset", "-q");
        File.Delete(Path.Combine(_repo, "src/staged.txt"));
        File.Delete(Path.Combine(_repo, "docs/b.txt"));
        Should.Throw<InvalidOperationException>(() => CreateGit().RequireClean()).Message.ShouldContain("docs/b.txt");
    }

    [Fact]
    public void RequireClean_IgnoredUntrackedFile_Passes()
    {
        Write(".gitignore", "*.log\n");
        Commit("ignore logs");
        Write("build.log", "noise");

        CreateGit().RequireClean();
    }

    [Fact]
    public void RequireClean_IgnoresAndoLogAndStateDirectory()
    {
        Write("build.csando.log", "log");
        Write(".ando/state.json", "{}");

        CreateGit().RequireClean();
        CreateGit().RequireClean(".");
        Write("src/new.txt", "x");
        Should.Throw<InvalidOperationException>(() => CreateGit().RequireClean());
    }

    [Fact]
    public void RequireClean_PathFilter_OnlyChecksThosePaths()
    {
        Write("docs/b.txt", "dirty");

        CreateGit().RequireClean("src");
        var ex = Should.Throw<InvalidOperationException>(() => CreateGit().RequireClean("src", "docs"));

        ex.Message.ShouldContain("docs/b.txt");
        ex.Message.ShouldContain("under src, docs");
    }

    [Fact]
    public void RequireClean_PathFilter_UntrackedFileUnderPath_Throws()
    {
        Write("src/new.txt", "x");

        CreateGit().RequireClean("docs");
        Should.Throw<InvalidOperationException>(() => CreateGit().RequireClean("src"));
    }

    // ---- ChangedSince / LastTag --------------------------------------------

    [Fact]
    public void LastTag_ReturnsMatchingTag()
    {
        CreateGit().LastTag("v*").ShouldBe("v1.0.0");
        CreateGit().LastTag().ShouldBe("v1.0.0");
    }

    [Fact]
    public void LastTag_NoMatch_ReturnsNull()
    {
        CreateGit().LastTag("release-*").ShouldBeNull();
    }

    [Fact]
    public void LastTag_NoTagsAtAll_ReturnsNull()
    {
        Git("tag", "-d", "v1.0.0");

        CreateGit().LastTag().ShouldBeNull();
    }

    [Fact]
    public void ChangedSince_ReportsChangesPerPath()
    {
        var git = CreateGit();

        git.ChangedSince("v1.0.0").ShouldBeTrue();
        git.ChangedSince("v1.0.0", "docs").ShouldBeTrue();
        git.ChangedSince("v1.0.0", "src").ShouldBeFalse();
        git.ChangedSince("v1.0.0", "src", "docs").ShouldBeTrue();
    }

    [Fact]
    public void ChangedSince_HeadItself_IsFalse()
    {
        CreateGit().ChangedSince("HEAD").ShouldBeFalse();
    }

    [Fact]
    public void ChangedSince_IgnoresUncommittedChanges()
    {
        Write("src/a.txt", "dirty");

        CreateGit().ChangedSince("v1.0.0", "src").ShouldBeFalse();
    }

    [Fact]
    public void ChangedSince_UnknownRef_Throws()
    {
        Should.Throw<InvalidOperationException>(() => CreateGit().ChangedSince("no-such-ref"));
    }
}
