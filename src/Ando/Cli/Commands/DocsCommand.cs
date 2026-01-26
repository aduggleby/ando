// =============================================================================
// DocsCommand.cs
//
// Summary: CLI command handler for 'ando docs'.
//
// This command uses Claude to review code changes and update documentation
// accordingly. It analyzes the diff since the last tag (or all changes) and
// prompts Claude to update markdown files, website pages, and examples.
//
// Design Decisions:
// - Uses git diff to determine what changed since last tag
// - Sends diff to Claude with instructions to update relevant docs
// - Auto-commits documentation changes if any were made
// - Non-fatal errors (Claude failure) return 0 to not block workflows
// - Can be called standalone or as part of the release workflow
// =============================================================================

using Ando.Hooks;
using Ando.Logging;
using Ando.Utilities;
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
    /// <returns>Exit code: 0 for success, 1 for errors.</returns>
    public async Task<int> ExecuteAsync(bool autoCommit = false)
    {
        try
        {
            // Check if we're in a git repository.
            if (!await _git.IsGitRepositoryAsync())
            {
                _logger.Error("Error: Not a git repository.");
                return 1;
            }

            var repoRoot = Directory.GetCurrentDirectory();

            // Initialize hook runner.
            var hookRunner = new HookRunner(repoRoot, _logger);
            var hookContext = new HookContext { Command = "docs" };

            // Run pre-hooks.
            if (!await hookRunner.RunHooksAsync(HookRunner.HookType.Pre, "docs", hookContext))
            {
                _logger.Error("Docs update aborted by pre-hook.");
                return 1;
            }

            // Get diff since last tag or all changes.
            var lastTag = await _git.GetLastTagAsync();
            var diff = lastTag != null
                ? await _git.GetDiffSinceTagAsync(lastTag)
                : await _git.GetDiffAsync();

            if (string.IsNullOrWhiteSpace(diff))
            {
                _logger.Info("No changes to analyze.");
                return 0;
            }

            _logger.Info(lastTag != null
                ? $"Analyzing changes since {lastTag}..."
                : "Analyzing all uncommitted changes...");

            var hasWebsite = Directory.Exists(Path.Combine(repoRoot, "website"));

            var prompt = $"""
                Review and update documentation based on these code changes.

                ## Code Changes
                ```
                {(diff.Length > 8000 ? diff[..8000] + "\n\n[Diff truncated]" : diff)}
                ```

                ## Instructions

                1. Find all markdown files (*.md) anywhere in the repository
                2. If a website/ folder exists, also check documentation pages there (*.mdx, *.astro)
                3. For each documentation file, determine if it needs updates based on the code changes
                4. Skip CHANGELOG.md (handled separately by version bump)

                ## What to Update

                **For website pages and README.md:**
                - New features that need documentation
                - Changed behavior that affects users
                - New options, parameters, or configuration
                - Examples that need updating
                - Ignore internal refactoring that doesn't affect public API

                ## Process

                For each file that needs changes:
                - Read the current content
                - Make the necessary updates
                - Show what you're changing before writing

                If no documentation updates are needed, just say so.
                """;

            AnsiConsole.MarkupLine("[dim]Claude is reviewing documentation...[/]");
            Console.WriteLine();

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
                Console.WriteLine();

                if (autoCommit)
                {
                    AnsiConsole.MarkupLine("[dim]Committing documentation changes...[/]");

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
}
