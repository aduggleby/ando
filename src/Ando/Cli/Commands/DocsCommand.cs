// =============================================================================
// DocsCommand.cs
//
// Summary: CLI command handler for 'ando docs'.
//
// This command uses Claude to review code changes and update documentation
// accordingly. It gets a list of commits since the last tag and instructs
// Claude to examine each commit, understand the changes, and update docs.
//
// Design Decisions:
// - Uses git log to get commits since last tag (not just diff)
// - Provides commit hashes so Claude can use `git show` to examine details
// - Lists changed files per commit for quick overview
// - Auto-commits documentation changes if any were made
// - Non-fatal errors (Claude failure) return 0 to not block workflows
// - Can be called standalone or as part of the release workflow
// =============================================================================

using Ando.Hooks;
using Ando.Logging;
using Ando.Utilities;
using Ando.Versioning;
using Spectre.Console;

namespace Ando.Cli.Commands;

/// <summary>
/// CLI command handler for 'ando docs'. Uses Claude to review code changes
/// and update documentation accordingly.
/// </summary>
public class DocsCommand
{
    private readonly CliProcessRunner _runner;
    private readonly CliGitOperations _git;
    private readonly IBuildLogger _logger;

    public DocsCommand(CliProcessRunner runner, IBuildLogger logger)
    {
        _runner = runner;
        _git = new CliGitOperations(runner);
        _logger = logger;
    }

    /// <summary>
    /// Executes the docs command.
    /// </summary>
    /// <param name="autoCommit">Automatically commit documentation changes.</param>
    /// <param name="workingDirectory">Working directory (defaults to current directory).</param>
    /// <returns>Exit code: 0 for success, 1 for errors.</returns>
    public async Task<int> ExecuteAsync(bool autoCommit = false, string? workingDirectory = null)
    {
        try
        {
            // Check if we're in a git repository.
            if (!await _git.IsGitRepositoryAsync())
            {
                _logger.Error("Error: Not a git repository.");
                return 1;
            }

            var repoRoot = workingDirectory ?? Directory.GetCurrentDirectory();

            // Sync version badges before running Claude.
            SyncVersionBadges(repoRoot);

            // Check for Claude permission (this command uses Claude for documentation updates).
            var claudeChecker = new ClaudePermissionChecker(_logger);
            var claudeResult = claudeChecker.CheckAndPrompt(repoRoot, "docs");
            if (!ClaudePermissionChecker.IsAllowed(claudeResult))
            {
                return 1;
            }

            // Initialize hook runner.
            var hookRunner = new HookRunner(repoRoot, _logger);
            var hookContext = new HookContext { Command = "docs" };

            // Run pre-hooks.
            if (!await hookRunner.RunHooksAsync(HookRunner.HookType.Pre, "docs", hookContext))
            {
                _logger.Error("Docs update aborted by pre-hook.");
                return 1;
            }

            // Get commits since last tag.
            var (commits, lastTag) = await _git.GetCommitsSinceLastTagAsync();

            if (commits.Count == 0)
            {
                _logger.Info("No commits to analyze.");
                return 0;
            }

            _logger.Info(lastTag != null
                ? $"Analyzing {commits.Count} commit(s) since {lastTag}..."
                : $"Analyzing {commits.Count} recent commit(s)...");

            // Format commits for the prompt.
            var commitList = string.Join("\n", commits.Select(c =>
                $"- [{c.Hash[..7]}] {c.Subject}\n  Files: {string.Join(", ", c.Files.Take(10))}{(c.Files.Count > 10 ? $" (+{c.Files.Count - 10} more)" : "")}"));

            var prompt = $"""
                Review and update documentation based on recent code changes.

                ## Commits Since {lastTag ?? "beginning"} ({commits.Count} total)

                {commitList}

                ## Instructions

                1. For each commit that looks like it adds or changes functionality:
                   - Use `git show <hash>` or read the changed files to understand what changed
                   - Determine if it impacts user-facing behavior or API

                2. Find all documentation that may need updates:
                   - Markdown files (*.md) anywhere in the repository
                   - Website pages in website/ folder (*.astro, *.js)
                   - The llms.txt file if it exists (must stay in sync with website)

                3. Skip these files (handled separately):
                   - CHANGELOG.md (handled by version bump)
                   - Internal refactoring that doesn't affect public API

                ## What to Update

                - New features, operations, or commands that need documentation
                - Changed behavior that affects users
                - New options, parameters, or configuration
                - Examples that need updating or adding
                - CLI help text if commands changed

                ## Process

                1. Examine each significant commit to understand the changes
                2. For each documentation file that needs updates:
                   - Read the current content
                   - Make the necessary updates
                   - Explain what you're changing

                If no documentation updates are needed, explain why.
                """;

            SafeConsoleWrite(() =>
            {
                AnsiConsole.MarkupLine("[dim]Claude is reviewing documentation...[/]");
                Console.WriteLine();
            });

            try
            {
                await _runner.RunClaudeAsync(prompt, timeoutMs: 300000, streamOutput: true);
            }
            catch (Exception ex)
            {
                _logger.Warning($"Documentation update skipped: {ex.Message}");
                // Non-fatal - return success.
                return 0;
            }

            // Check if Claude made any changes.
            if (await _git.HasUncommittedChangesAsync())
            {
                SafeConsoleWrite(() => Console.WriteLine());

                if (autoCommit)
                {
                    SafeConsoleWrite(() => AnsiConsole.MarkupLine("[dim]Committing documentation changes...[/]"));

                    try
                    {
                        await _git.StageAllAsync();
                        await _git.CommitAsync("docs: update documentation");
                        _logger.Info("Documentation changes committed.");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warning($"Failed to commit documentation changes: {ex.Message}");
                        // Non-fatal.
                    }
                }
                else
                {
                    _logger.Info("Documentation updated. Review changes and commit when ready.");
                }
            }
            else
            {
                _logger.Info("No documentation changes needed.");
            }

            // Run post-hooks.
            await hookRunner.RunHooksAsync(HookRunner.HookType.Post, "docs", hookContext);

            return 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error: {ex.Message}");
            return 1;
        }
    }

    /// <summary>
    /// Detects the current project version and syncs version badges in documentation files.
    /// All failures are non-fatal — logged as warnings and do not block the docs command.
    /// </summary>
    private void SyncVersionBadges(string repoRoot)
    {
        try
        {
            var buildScript = Path.Combine(repoRoot, "build.csando");
            if (!File.Exists(buildScript))
                return;

            var detector = new ProjectDetector();
            var projects = detector.DetectProjects(buildScript);
            if (projects.Count == 0)
                return;

            var reader = new VersionReader();
            var fullPath = Path.Combine(repoRoot, projects[0].Path);
            var currentVersion = reader.ReadVersion(fullPath, projects[0].Type);
            if (currentVersion == null)
                return;

            var updater = new DocumentationUpdater(repoRoot);
            var results = updater.SyncVersionBadges(currentVersion);

            foreach (var result in results)
            {
                if (result.Success && result.Error == null)
                {
                    _logger.Info($"Updated version badge in {result.FilePath} to {currentVersion}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Version badge sync skipped: {ex.Message}");
        }
    }

    /// <summary>
    /// Safely writes to console, ignoring ObjectDisposedException when console is unavailable.
    /// This can happen during parallel test execution when console streams are closed.
    /// </summary>
    private static void SafeConsoleWrite(Action writeAction)
    {
        try
        {
            writeAction();
        }
        catch (ObjectDisposedException)
        {
            // Console output unavailable - continue silently.
        }
    }
}
