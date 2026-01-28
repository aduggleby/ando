// =============================================================================
// CliGitOperationsTests.cs
//
// Unit tests for CliGitOperations git command wrapper.
// =============================================================================

using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Shouldly;

namespace Ando.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class CliGitOperationsTests
{
    private readonly MockCliProcessRunner _runner;
    private readonly CliGitOperations _git;

    public CliGitOperationsTests()
    {
        _runner = new MockCliProcessRunner();
        _git = new CliGitOperations(_runner);
    }

    #region HasUncommittedChangesAsync Tests

    [Fact]
    public async Task HasUncommittedChangesAsync_WithChanges_ReturnsTrue()
    {
        _runner.SetOutput("git", "status --porcelain", " M file.txt\n");

        var result = await _git.HasUncommittedChangesAsync();

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task HasUncommittedChangesAsync_NoChanges_ReturnsFalse()
    {
        _runner.SetOutput("git", "status --porcelain", "");

        var result = await _git.HasUncommittedChangesAsync();

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task HasUncommittedChangesAsync_OnlyWhitespace_ReturnsFalse()
    {
        _runner.SetOutput("git", "status --porcelain", "   \n  \n");

        var result = await _git.HasUncommittedChangesAsync();

        result.ShouldBeFalse();
    }

    #endregion

    #region GetChangedFilesAsync Tests

    [Fact]
    public async Task GetChangedFilesAsync_ParsesSimpleStatus()
    {
        _runner.SetOutput("git", "status --porcelain", " M src/file.cs\n A new.txt\n D deleted.cs");

        var files = await _git.GetChangedFilesAsync();

        files.ShouldContain("src/file.cs");
        files.ShouldContain("new.txt");
        files.ShouldContain("deleted.cs");
        files.Count.ShouldBe(3);
    }

    [Fact]
    public async Task GetChangedFilesAsync_ParsesRenames()
    {
        _runner.SetOutput("git", "status --porcelain", "R  old.txt -> new.txt");

        var files = await _git.GetChangedFilesAsync();

        // Should return the new name, not the old.
        files.ShouldContain("new.txt");
        files.ShouldNotContain("old.txt");
    }

    [Fact]
    public async Task GetChangedFilesAsync_EmptyStatus_ReturnsEmptyList()
    {
        _runner.SetOutput("git", "status --porcelain", "");

        var files = await _git.GetChangedFilesAsync();

        files.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetChangedFilesAsync_HandlesUntrackedFiles()
    {
        _runner.SetOutput("git", "status --porcelain", "?? untracked.txt\n M tracked.cs");

        var files = await _git.GetChangedFilesAsync();

        files.ShouldContain("untracked.txt");
        files.ShouldContain("tracked.cs");
    }

    #endregion

    #region GetDiffAsync Tests

    [Fact]
    public async Task GetDiffAsync_CombinesStagedAndUnstaged()
    {
        _runner.SetOutput("git", "diff --cached -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "staged diff\n");
        _runner.SetOutput("git", "diff -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "unstaged diff\n");

        var diff = await _git.GetDiffAsync();

        diff.ShouldContain("staged diff");
        diff.ShouldContain("unstaged diff");
    }

    [Fact]
    public async Task GetDiffAsync_EmptyDiffs_ReturnsEmpty()
    {
        _runner.SetOutput("git", "diff --cached -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "");
        _runner.SetOutput("git", "diff -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "");

        var diff = await _git.GetDiffAsync();

        diff.ShouldBeEmpty();
    }

    #endregion

    #region GetLastTagAsync Tests

    [Fact]
    public async Task GetLastTagAsync_WithTag_ReturnsTag()
    {
        _runner.SetResult("git", "describe --tags --abbrev=0", new CliProcessRunner.ProcessResult(0, "v1.2.3\n", ""));

        var tag = await _git.GetLastTagAsync();

        tag.ShouldBe("v1.2.3");
    }

    [Fact]
    public async Task GetLastTagAsync_NoTags_ReturnsNull()
    {
        _runner.SetResult("git", "describe --tags --abbrev=0", new CliProcessRunner.ProcessResult(128, "", "fatal: No names found"));

        var tag = await _git.GetLastTagAsync();

        tag.ShouldBeNull();
    }

    #endregion

    #region GetCurrentBranchAsync Tests

    [Fact]
    public async Task GetCurrentBranchAsync_ReturnsBranchName()
    {
        _runner.SetOutput("git", "branch --show-current", "main\n");

        var branch = await _git.GetCurrentBranchAsync();

        branch.ShouldBe("main");
    }

    [Fact]
    public async Task GetCurrentBranchAsync_FeatureBranch_ReturnsBranchName()
    {
        _runner.SetOutput("git", "branch --show-current", "feature/new-feature\n");

        var branch = await _git.GetCurrentBranchAsync();

        branch.ShouldBe("feature/new-feature");
    }

    #endregion

    #region HasRemoteTrackingAsync Tests

    [Fact]
    public async Task HasRemoteTrackingAsync_WithTracking_ReturnsTrue()
    {
        _runner.SetResult("git", "rev-parse --abbrev-ref @{upstream}", new CliProcessRunner.ProcessResult(0, "origin/main\n", ""));

        var hasTracking = await _git.HasRemoteTrackingAsync();

        hasTracking.ShouldBeTrue();
    }

    [Fact]
    public async Task HasRemoteTrackingAsync_NoTracking_ReturnsFalse()
    {
        _runner.SetResult("git", "rev-parse --abbrev-ref @{upstream}", new CliProcessRunner.ProcessResult(128, "", "fatal: no upstream"));

        var hasTracking = await _git.HasRemoteTrackingAsync();

        hasTracking.ShouldBeFalse();
    }

    #endregion

    #region GetRemoteTrackingBranchAsync Tests

    [Fact]
    public async Task GetRemoteTrackingBranchAsync_WithTracking_ReturnsBranch()
    {
        _runner.SetResult("git", "rev-parse --abbrev-ref @{upstream}", new CliProcessRunner.ProcessResult(0, "origin/main\n", ""));

        var remote = await _git.GetRemoteTrackingBranchAsync();

        remote.ShouldBe("origin/main");
    }

    [Fact]
    public async Task GetRemoteTrackingBranchAsync_NoTracking_ReturnsNull()
    {
        _runner.SetResult("git", "rev-parse --abbrev-ref @{upstream}", new CliProcessRunner.ProcessResult(128, "", "fatal: no upstream"));

        var remote = await _git.GetRemoteTrackingBranchAsync();

        remote.ShouldBeNull();
    }

    #endregion

    #region IsGitRepositoryAsync Tests

    [Fact]
    public async Task IsGitRepositoryAsync_InRepo_ReturnsTrue()
    {
        _runner.SetResult("git", "rev-parse --git-dir", new CliProcessRunner.ProcessResult(0, ".git\n", ""));

        var isRepo = await _git.IsGitRepositoryAsync();

        isRepo.ShouldBeTrue();
    }

    [Fact]
    public async Task IsGitRepositoryAsync_NotInRepo_ReturnsFalse()
    {
        _runner.SetResult("git", "rev-parse --git-dir", new CliProcessRunner.ProcessResult(128, "", "fatal: not a git repository"));

        var isRepo = await _git.IsGitRepositoryAsync();

        isRepo.ShouldBeFalse();
    }

    #endregion

    #region CommitAsync Tests

    [Fact]
    public async Task CommitAsync_Success_NoException()
    {
        _runner.SetResult("git", "commit -F -", new CliProcessRunner.ProcessResult(0, "[main abc123] Test commit\n", ""));

        await Should.NotThrowAsync(async () => await _git.CommitAsync("Test commit"));
    }

    [Fact]
    public async Task CommitAsync_Failure_ThrowsException()
    {
        _runner.SetResult("git", "commit -F -", new CliProcessRunner.ProcessResult(1, "", "nothing to commit"));

        await Should.ThrowAsync<Exception>(async () => await _git.CommitAsync("Test commit"));
    }

    [Fact]
    public async Task CommitAsync_PassesMessageViaStdin()
    {
        _runner.SetResult("git", "commit -F -", new CliProcessRunner.ProcessResult(0, "committed\n", ""));

        await _git.CommitAsync("Multi-line\ncommit\nmessage");

        var commit = _runner.ExecutedProcesses.First(p => p.FileName == "git" && p.Arguments.Contains("commit"));
        commit.Stdin.ShouldBe("Multi-line\ncommit\nmessage");
    }

    #endregion

    #region PushAsync Tests

    [Fact]
    public async Task PushAsync_Success_NoException()
    {
        _runner.SetResult("git", "push", new CliProcessRunner.ProcessResult(0, "Everything up-to-date\n", ""));

        await Should.NotThrowAsync(async () => await _git.PushAsync());
    }

    [Fact]
    public async Task PushAsync_Failure_ThrowsException()
    {
        _runner.SetResult("git", "push", new CliProcessRunner.ProcessResult(1, "", "rejected: non-fast-forward"));

        await Should.ThrowAsync<Exception>(async () => await _git.PushAsync());
    }

    #endregion

    #region GetCurrentCommitShortAsync Tests

    [Fact]
    public async Task GetCurrentCommitShortAsync_ReturnsShortHash()
    {
        _runner.SetOutput("git", "rev-parse --short HEAD", "abc1234\n");

        var hash = await _git.GetCurrentCommitShortAsync();

        hash.ShouldBe("abc1234");
    }

    #endregion

    #region GetCommitMessagesSinceTagAsync Tests

    [Fact]
    public async Task GetCommitMessagesSinceTagAsync_ReturnsCommitMessages()
    {
        _runner.SetResult("git", "log v1.0.0..HEAD --pretty=format:%s",
            new CliProcessRunner.ProcessResult(0, "feat: add new feature\nfix: bug fix\nchore: update deps\n", ""));

        var messages = await _git.GetCommitMessagesSinceTagAsync("v1.0.0");

        messages.Count.ShouldBe(3);
        messages.ShouldContain("feat: add new feature");
        messages.ShouldContain("fix: bug fix");
        messages.ShouldContain("chore: update deps");
    }

    [Fact]
    public async Task GetCommitMessagesSinceTagAsync_NoCommits_ReturnsEmptyList()
    {
        _runner.SetResult("git", "log v1.0.0..HEAD --pretty=format:%s",
            new CliProcessRunner.ProcessResult(0, "", ""));

        var messages = await _git.GetCommitMessagesSinceTagAsync("v1.0.0");

        messages.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetCommitMessagesSinceTagAsync_TagNotFound_ReturnsEmptyList()
    {
        _runner.SetResult("git", "log v1.0.0..HEAD --pretty=format:%s",
            new CliProcessRunner.ProcessResult(128, "", "fatal: bad revision 'v1.0.0..HEAD'"));

        var messages = await _git.GetCommitMessagesSinceTagAsync("v1.0.0");

        messages.ShouldBeEmpty();
    }

    #endregion

    #region TagExistsAsync Tests

    [Fact]
    public async Task TagExistsAsync_TagExists_ReturnsTrue()
    {
        _runner.SetResult("git", "rev-parse v1.0.0",
            new CliProcessRunner.ProcessResult(0, "abc123def456\n", ""));

        var exists = await _git.TagExistsAsync("v1.0.0");

        exists.ShouldBeTrue();
    }

    [Fact]
    public async Task TagExistsAsync_TagNotFound_ReturnsFalse()
    {
        _runner.SetResult("git", "rev-parse v1.0.0",
            new CliProcessRunner.ProcessResult(128, "", "fatal: bad revision 'v1.0.0'"));

        var exists = await _git.TagExistsAsync("v1.0.0");

        exists.ShouldBeFalse();
    }

    #endregion

    #region GetChangedFilesSinceTagAsync Tests

    [Fact]
    public async Task GetChangedFilesSinceTagAsync_ReturnsChangedFiles()
    {
        _runner.SetResult("git", "diff --name-only v1.0.0..HEAD",
            new CliProcessRunner.ProcessResult(0, "src/File1.cs\nsrc/File2.cs\nREADME.md\n", ""));

        var files = await _git.GetChangedFilesSinceTagAsync("v1.0.0");

        files.Count.ShouldBe(3);
        files.ShouldContain("src/File1.cs");
        files.ShouldContain("src/File2.cs");
        files.ShouldContain("README.md");
    }

    [Fact]
    public async Task GetChangedFilesSinceTagAsync_NoChanges_ReturnsEmptyList()
    {
        _runner.SetResult("git", "diff --name-only v1.0.0..HEAD",
            new CliProcessRunner.ProcessResult(0, "", ""));

        var files = await _git.GetChangedFilesSinceTagAsync("v1.0.0");

        files.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetChangedFilesSinceTagAsync_TagNotFound_ReturnsEmptyList()
    {
        _runner.SetResult("git", "diff --name-only v1.0.0..HEAD",
            new CliProcessRunner.ProcessResult(128, "", "fatal: bad revision 'v1.0.0..HEAD'"));

        var files = await _git.GetChangedFilesSinceTagAsync("v1.0.0");

        files.ShouldBeEmpty();
    }

    #endregion

    #region GetChangesSinceLastTagAsync Tests

    [Fact]
    public async Task GetChangesSinceLastTagAsync_NoTags_ReturnsHasChangesTrue()
    {
        _runner.SetResult("git", "describe --tags --abbrev=0", new CliProcessRunner.ProcessResult(128, "", "fatal: No names found"));

        var (hasChanges, lastTag, commitCount) = await _git.GetChangesSinceLastTagAsync();

        hasChanges.ShouldBeTrue();
        lastTag.ShouldBeNull();
        commitCount.ShouldBe(-1);
    }

    [Fact]
    public async Task GetChangesSinceLastTagAsync_WithCommitsSinceTag_ReturnsHasChangesTrue()
    {
        _runner.SetResult("git", "describe --tags --abbrev=0", new CliProcessRunner.ProcessResult(0, "v1.0.0\n", ""));
        _runner.SetResult("git", "log v1.0.0..HEAD --pretty=format:%s",
            new CliProcessRunner.ProcessResult(0, "feat: add feature\nfix: bug fix\n", ""));

        var (hasChanges, lastTag, commitCount) = await _git.GetChangesSinceLastTagAsync();

        hasChanges.ShouldBeTrue();
        lastTag.ShouldBe("v1.0.0");
        commitCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetChangesSinceLastTagAsync_NoCommitsSinceTag_ReturnsHasChangesFalse()
    {
        _runner.SetResult("git", "describe --tags --abbrev=0", new CliProcessRunner.ProcessResult(0, "v1.0.0\n", ""));
        _runner.SetResult("git", "log v1.0.0..HEAD --pretty=format:%s",
            new CliProcessRunner.ProcessResult(0, "", ""));

        var (hasChanges, lastTag, commitCount) = await _git.GetChangesSinceLastTagAsync();

        hasChanges.ShouldBeFalse();
        lastTag.ShouldBe("v1.0.0");
        commitCount.ShouldBe(0);
    }

    #endregion
}
