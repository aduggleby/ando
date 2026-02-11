// =============================================================================
// GitOperationsIntegrationTests.cs
//
// Integration tests for GitOperations against real git repositories.
// Verifies tag push behavior with an existing remote tag.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Shouldly;
using Xunit.Sdk;

namespace Ando.Tests.Integration;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Integration")]
public class GitOperationsIntegrationTests : IDisposable
{
    private readonly string? _tempRoot;
    private readonly string? _remoteRepoPath;
    private readonly string? _workRepoPath;
    private readonly CliProcessRunner _runner;
    private readonly bool _gitAvailable;

    public GitOperationsIntegrationTests()
    {
        _runner = new CliProcessRunner();
        _gitAvailable = IsGitAvailable();

        if (!_gitAvailable)
        {
            return;
        }

        _tempRoot = Path.Combine(Path.GetTempPath(), $"ando-git-ops-{Guid.NewGuid():N}");
        _remoteRepoPath = Path.Combine(_tempRoot, "remote.git");
        _workRepoPath = Path.Combine(_tempRoot, "work");

        Directory.CreateDirectory(_tempRoot);
        Directory.CreateDirectory(_workRepoPath);

        RunGit("init --bare remote.git", _tempRoot);
        RunGit("init", _workRepoPath);
        RunGit("config user.email test@example.com", _workRepoPath);
        RunGit("config user.name Test User", _workRepoPath);
        RunGit($"remote add origin \"{_remoteRepoPath}\"", _workRepoPath);

        File.WriteAllText(Path.Combine(_workRepoPath, "README.md"), "initial");
        RunGit("add README.md", _workRepoPath);
        RunGit("commit -m \"initial commit\"", _workRepoPath);
        RunGit("push -u origin HEAD", _workRepoPath);
    }

    [SkippableFact]
    public async Task PushTags_WhenRemoteAlreadyHasTag_PushesOnlyMissingTags()
    {
        Skip.If(!_gitAvailable, "Git is not available in this environment");

        // Seed remote with an existing tag that matches the local tag.
        RunGit("tag v1.0.0", _workRepoPath!);
        RunGit("push origin refs/tags/v1.0.0", _workRepoPath!);

        // Add a new local tag that should be pushed by Git.PushTags.
        RunGit("tag v1.0.1", _workRepoPath!);

        var registry = new StepRegistry();
        var logger = new TestLogger();
        var git = new GitOperations(registry, logger, () => new MockExecutor());

        git.PushTags();

        var originalDir = Directory.GetCurrentDirectory();
        try
        {
            Directory.SetCurrentDirectory(_workRepoPath!);
            var success = await registry.Steps.Single().Execute();
            success.ShouldBeTrue();
        }
        finally
        {
            Directory.SetCurrentDirectory(originalDir);
        }

        var remoteTags = await _runner.RunAsync(
            "git",
            $"ls-remote --tags --refs \"{_remoteRepoPath}\"");

        remoteTags.ExitCode.ShouldBe(0);
        remoteTags.Output.ShouldContain("refs/tags/v1.0.0");
        remoteTags.Output.ShouldContain("refs/tags/v1.0.1");
    }

    public void Dispose()
    {
        if (_tempRoot == null || !Directory.Exists(_tempRoot))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(_tempRoot, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(_tempRoot, recursive: true);
    }

    private static bool IsGitAvailable()
    {
        try
        {
            var runner = new CliProcessRunner();
            var result = runner.RunAsync("git", "--version").GetAwaiter().GetResult();
            return result.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private void RunGit(string args, string workingDirectory)
    {
        var result = _runner.RunAsync("git", args, workingDirectory: workingDirectory).GetAwaiter().GetResult();
        if (result.ExitCode != 0)
        {
            throw new Exception($"Git command failed: git {args}\n{result.Error}");
        }
    }
}
