// =============================================================================
// CommitCommandTests.cs
//
// Unit tests for CommitCommand AI-generated commit message logic.
// =============================================================================

using Ando.Cli.Commands;
using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Shouldly;

namespace Ando.Tests.Unit.Cli.Commands;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Unit")]
public class CommitCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _originalDir;
    private readonly TestLogger _logger;
    private readonly MockCliProcessRunner _runner;

    public CommitCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"commit-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_testDir);

        _logger = new TestLogger();
        _runner = new MockCliProcessRunner();
    }

    public void Dispose()
    {
        try
        {
            Directory.SetCurrentDirectory(_originalDir);
        }
        catch (DirectoryNotFoundException)
        {
            // Original directory no longer exists (e.g., another test deleted it).
        }

        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    #region Repository Detection Tests

    [Fact]
    public async Task ExecuteAsync_NotGitRepository_ReturnsError()
    {
        _runner.SetResult("git", "rev-parse --git-dir", new CliProcessRunner.ProcessResult(128, "", "fatal: not a git repository"));

        var command = new CommitCommand(_runner, _logger);
        var result = await command.ExecuteAsync();

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Not a git repository"));
    }

    #endregion

    #region No Changes Tests

    [Fact]
    public async Task ExecuteAsync_NoChanges_ReturnsSuccessWithMessage()
    {
        SetupGitRepo();
        _runner.SetOutput("git", "status --porcelain", "");

        var command = new CommitCommand(_runner, _logger);
        var result = await command.ExecuteAsync();

        result.ShouldBe(0);
        _logger.InfoMessages.ShouldContain(m => m.Contains("Nothing to commit"));
    }

    #endregion

    #region Change Detection Tests

    [Fact]
    public async Task ExecuteAsync_WithChanges_ShowsAnalyzingMessage()
    {
        SetupGitRepo();
        SetupWithChanges();

        var command = new CommitCommand(_runner, _logger);
        // This will fail at Claude invocation, but we're testing the setup.
        try
        {
            await command.ExecuteAsync();
        }
        catch
        {
            // Expected to fail without Claude CLI.
        }

        _logger.InfoMessages.ShouldContain(m => m.Contains("Analyzing changes"));
    }

    [Fact]
    public async Task ExecuteAsync_WithChanges_GetsDiff()
    {
        SetupGitRepo();
        SetupWithChanges();
        _runner.SetOutput("git", "diff --cached -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "staged diff");
        _runner.SetOutput("git", "diff -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "unstaged diff");

        var command = new CommitCommand(_runner, _logger);
        try
        {
            await command.ExecuteAsync();
        }
        catch
        {
            // Expected to fail without Claude CLI.
        }

        _runner.WasExecuted("git", "diff").ShouldBeTrue();
    }

    #endregion

    #region Claude CLI Integration Tests

    [Fact]
    public async Task ExecuteAsync_ClaudeFails_ReturnsErrorWithHelpfulMessage()
    {
        SetupGitRepo();
        SetupWithChanges();
        _runner.SetFailure("claude", "-p --dangerously-skip-permissions", "command not found");

        var command = new CommitCommand(_runner, _logger);
        var result = await command.ExecuteAsync();

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("generate commit message"));
        _logger.ErrorMessages.ShouldContain(m => m.Contains("npm install"));
    }

    #endregion

    #region Git Operations Tests

    [Fact]
    public async Task ExecuteAsync_CallsGitStatusAndDiff()
    {
        SetupGitRepo();
        SetupWithChanges();

        var command = new CommitCommand(_runner, _logger);
        try
        {
            await command.ExecuteAsync();
        }
        catch
        {
            // Expected to fail without Claude.
        }

        _runner.WasExecuted("git", "status --porcelain").ShouldBeTrue();
    }

    #endregion

    #region Helper Methods

    private void SetupGitRepo()
    {
        _runner.SetResult("git", "rev-parse --git-dir", new CliProcessRunner.ProcessResult(0, ".git", ""));
    }

    private void SetupWithChanges()
    {
        _runner.SetOutput("git", "status --porcelain", " M src/file.cs\n");
        _runner.SetOutput("git", "diff --cached -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "");
        _runner.SetOutput("git", "diff -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "diff content");
        // Create ando.config with Claude permission to skip the prompt.
        File.WriteAllText(Path.Combine(_testDir, "ando.config"), """{"allowClaude": true}""");
    }

    #endregion
}
