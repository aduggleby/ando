// =============================================================================
// DocsCommandTests.cs
//
// Unit tests for DocsCommand documentation update logic.
// =============================================================================

using Ando.Cli.Commands;
using Ando.Config;
using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Shouldly;

namespace Ando.Tests.Unit.Cli.Commands;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Unit")]
public class DocsCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _originalDir;
    private readonly TestLogger _logger;
    private readonly MockCliProcessRunner _runner;

    public DocsCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"docs-cmd-{Guid.NewGuid():N}");
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

        var command = new DocsCommand(_runner, _logger);
        var result = await command.ExecuteAsync();

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Not a git repository"));
    }

    #endregion

    #region No Changes Tests

    [Fact]
    public async Task ExecuteAsync_NoChanges_ReturnsSuccess()
    {
        SetupGitRepo();
        SetupNoChanges();

        var command = new DocsCommand(_runner, _logger);
        var result = await command.ExecuteAsync();

        result.ShouldBe(0);
        _logger.InfoMessages.ShouldContain(m => m.Contains("No changes to analyze"));
    }

    #endregion

    #region Change Detection Tests

    [Fact]
    public async Task ExecuteAsync_WithChanges_ShowsAnalyzingMessage()
    {
        SetupGitRepo();
        SetupWithChanges();

        var command = new DocsCommand(_runner, _logger);
        // This will fail at Claude invocation, but we're testing the setup.
        try
        {
            await command.ExecuteAsync();
        }
        catch
        {
            // Expected to fail without Claude CLI.
        }

        _logger.InfoMessages.ShouldContain(m => m.Contains("Analyzing"));
    }

    [Fact]
    public async Task ExecuteAsync_WithTag_AnalyzesSinceTag()
    {
        SetupGitRepo();
        _runner.SetOutput("git", "describe --tags --abbrev=0", "v1.0.0\n");
        _runner.SetOutput("git", "diff v1.0.0..HEAD", "diff content");
        // Create ando.config with Claude permission using ProjectConfig.
        var config = new ProjectConfig { AllowClaude = true };
        config.Save(_testDir);

        var command = new DocsCommand(_runner, _logger);
        try
        {
            await command.ExecuteAsync();
        }
        catch
        {
            // Expected to fail without Claude CLI.
        }

        _logger.InfoMessages.ShouldContain(m => m.Contains("v1.0.0"));
    }

    #endregion

    #region Claude CLI Integration Tests

    [Fact]
    public async Task ExecuteAsync_ClaudeFails_ReturnsSuccessWithWarning()
    {
        SetupGitRepo();
        SetupWithChanges();
        // Mock Claude failure (when RunClaudeAsync is called, it will throw).
        _runner.SetResult("claude", "-p --dangerously-skip-permissions", new CliProcessRunner.ProcessResult(1, "", "command not found"));

        var command = new DocsCommand(_runner, _logger);
        var result = await command.ExecuteAsync();

        // DocsCommand returns success even when Claude fails (non-fatal).
        result.ShouldBe(0);
        _logger.WarningMessages.ShouldContain(m => m.Contains("Documentation update skipped"));
    }

    #endregion

    #region Helper Methods

    private void SetupGitRepo()
    {
        _runner.SetResult("git", "rev-parse --git-dir", new CliProcessRunner.ProcessResult(0, ".git", ""));
    }

    private void SetupNoChanges()
    {
        // No tag exists.
        _runner.SetResult("git", "describe --tags --abbrev=0", new CliProcessRunner.ProcessResult(128, "", "fatal: No names found"));
        // GetDiffAsync calls two git diff commands - both return empty.
        _runner.SetOutput("git", "diff --cached -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "");
        _runner.SetOutput("git", "diff -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "");
        // Create ando.config with Claude permission using ProjectConfig.
        var config = new ProjectConfig { AllowClaude = true };
        config.Save(_testDir);
    }

    private void SetupWithChanges()
    {
        // No tag exists.
        _runner.SetResult("git", "describe --tags --abbrev=0", new CliProcessRunner.ProcessResult(128, "", "fatal: No names found"));
        // GetDiffAsync calls two git diff commands - unstaged has changes.
        _runner.SetOutput("git", "diff --cached -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "");
        _runner.SetOutput("git", "diff -- . :(exclude)package-lock.json :(exclude)yarn.lock :(exclude)pnpm-lock.yaml :(exclude)*.min.js :(exclude)*.min.css", "diff content here");
        // Create ando.config with Claude permission using ProjectConfig.
        var config = new ProjectConfig { AllowClaude = true };
        config.Save(_testDir);
    }

    #endregion
}
