// =============================================================================
// CliGitOperationsTests.cs
//
// Integration tests for CliGitOperations using actual git repositories.
// =============================================================================

using Ando.Utilities;
using Shouldly;

namespace Ando.Tests.Integration;

[Trait("Category", "Integration")]
public class CliGitOperationsTests : IDisposable
{
    private readonly string _testDir;
    private readonly CliProcessRunner _runner;
    private readonly CliGitOperations _git;

    public CliGitOperationsTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"ando-git-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);

        _runner = new CliProcessRunner();
        _git = new CliGitOperations(_runner);

        // Initialize git repo in test directory.
        InitGitRepo();
    }

    public void Dispose()
    {
        // Clean up test directory.
        if (Directory.Exists(_testDir))
        {
            // Git creates read-only files, need to make them writable first.
            foreach (var file in Directory.GetFiles(_testDir, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }
            Directory.Delete(_testDir, recursive: true);
        }
    }

    private void InitGitRepo()
    {
        RunGit("init");
        RunGit("config user.email test@example.com");
        RunGit("config user.name Test");
    }

    private void RunGit(string args)
    {
        var result = _runner.RunAsync("git", args, workingDirectory: _testDir).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
            throw new Exception($"Git command failed: {result.Error}");
    }

    private void CreateFile(string name, string content = "test content")
    {
        File.WriteAllText(Path.Combine(_testDir, name), content);
    }

    [Fact]
    public async Task IsGitRepositoryAsync_InGitRepo_ReturnsTrue()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);
            var result = await _git.IsGitRepositoryAsync();
            result.ShouldBeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task IsGitRepositoryAsync_NotInGitRepo_ReturnsFalse()
    {
        var nonGitDir = Path.Combine(Path.GetTempPath(), $"non-git-{Guid.NewGuid():N}");
        Directory.CreateDirectory(nonGitDir);
        try
        {
            var originalDir = Directory.GetCurrentDirectory();
            try
            {
                Directory.SetCurrentDirectory(nonGitDir);
                var result = await _git.IsGitRepositoryAsync();
                result.ShouldBeFalse();
            }
            finally
            {
                Directory.SetCurrentDirectory(originalDir);
            }
        }
        finally
        {
            Directory.Delete(nonGitDir);
        }
    }

    [Fact]
    public async Task HasUncommittedChangesAsync_CleanRepo_ReturnsFalse()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            // Create initial commit so repo is not empty.
            CreateFile("initial.txt");
            RunGit("add .");
            RunGit("commit -m \"initial\"");

            var result = await _git.HasUncommittedChangesAsync();
            result.ShouldBeFalse();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task HasUncommittedChangesAsync_WithChanges_ReturnsTrue()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            CreateFile("test.txt");

            var result = await _git.HasUncommittedChangesAsync();
            result.ShouldBeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task GetChangedFilesAsync_ReturnsChangedFiles()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            CreateFile("file1.txt");
            CreateFile("file2.txt");

            var files = await _git.GetChangedFilesAsync();

            files.ShouldContain("file1.txt");
            files.ShouldContain("file2.txt");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task StageAllAsync_StagesAllFiles()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            CreateFile("staged.txt");
            await _git.StageAllAsync();

            var status = await _git.GetStatusAsync();
            status.ShouldContain("A  staged.txt");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task CommitAsync_CreatesCommit()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            CreateFile("commit-test.txt");
            await _git.StageAllAsync();
            await _git.CommitAsync("Test commit message");

            // Verify commit was created.
            var hasChanges = await _git.HasUncommittedChangesAsync();
            hasChanges.ShouldBeFalse();

            // Verify commit message.
            var logResult = await _runner.RunAsync("git", "log -1 --oneline", workingDirectory: _testDir);
            logResult.Output.ShouldContain("Test commit message");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task GetCurrentBranchAsync_ReturnsCurrentBranch()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            // Create initial commit (required for branch to exist).
            CreateFile("initial.txt");
            RunGit("add .");
            RunGit("commit -m \"initial\"");

            var branch = await _git.GetCurrentBranchAsync();

            // Default branch could be 'main' or 'master' depending on git version.
            (branch == "main" || branch == "master").ShouldBeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task GetDiffAsync_ReturnsChanges()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            // Create initial commit.
            CreateFile("initial.txt", "original content");
            RunGit("add .");
            RunGit("commit -m \"initial\"");

            // Modify file.
            CreateFile("initial.txt", "modified content");

            var diff = await _git.GetDiffAsync();

            diff.ShouldContain("modified content");
            diff.ShouldContain("-original content");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task GetLastTagAsync_NoTags_ReturnsNull()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            // Create initial commit.
            CreateFile("initial.txt");
            RunGit("add .");
            RunGit("commit -m \"initial\"");

            var tag = await _git.GetLastTagAsync();
            tag.ShouldBeNull();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task GetLastTagAsync_WithTag_ReturnsTag()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            // Create initial commit and tag.
            CreateFile("initial.txt");
            RunGit("add .");
            RunGit("commit -m \"initial\"");
            RunGit("tag v1.0.0");

            var tag = await _git.GetLastTagAsync();
            tag.ShouldBe("v1.0.0");
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }

    [Fact]
    public async Task HasRemoteTrackingAsync_NoRemote_ReturnsFalse()
    {
        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_testDir);

            // Create initial commit.
            CreateFile("initial.txt");
            RunGit("add .");
            RunGit("commit -m \"initial\"");

            var hasRemote = await _git.HasRemoteTrackingAsync();
            hasRemote.ShouldBeFalse();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }
    }
}
