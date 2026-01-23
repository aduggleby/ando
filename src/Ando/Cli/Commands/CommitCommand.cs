// =============================================================================
// CommitCommand.cs
//
// Summary: CLI command handler for 'ando commit'.
//
// This command commits all staged and unstaged changes with an AI-generated
// commit message using Claude. It displays the generated message and prompts
// for user confirmation before committing.
//
// Design Decisions:
// - Displays changed files before generating message for context
// - Shows generated message with visual separators for clarity
// - Requires user confirmation (Y/n) before committing
// - Stages all changes before committing (git add -A)
// - Returns 0 for success or user cancellation, 1 for errors
// =============================================================================

using Ando.AI;
using Ando.Logging;
using Ando.Utilities;

namespace Ando.Cli.Commands;

/// <summary>
/// CLI command handler for 'ando commit'. Commits all changes with an
/// AI-generated commit message using Claude.
/// </summary>
public class CommitCommand
{
    private readonly CliGitOperations _git;
    private readonly CommitMessageGenerator _messageGenerator;
    private readonly IBuildLogger _logger;

    public CommitCommand(CliProcessRunner runner, IBuildLogger logger)
    {
        _git = new CliGitOperations(runner);
        _messageGenerator = new CommitMessageGenerator(runner);
        _logger = logger;
    }

    /// <summary>
    /// Executes the commit command.
    /// </summary>
    /// <returns>Exit code: 0 for success or cancellation, 1 for errors.</returns>
    public async Task<int> ExecuteAsync()
    {
        try
        {
            // Check if we're in a git repository.
            if (!await _git.IsGitRepositoryAsync())
            {
                _logger.Error("Error: Not a git repository.");
                return 1;
            }

            // Check for uncommitted changes.
            if (!await _git.HasUncommittedChangesAsync())
            {
                _logger.Info("Nothing to commit. Working tree is clean.");
                return 0;
            }

            // Display analyzing message.
            _logger.Info("Analyzing changes...");
            Console.WriteLine();

            // Show changed files.
            var changedFiles = await _git.GetChangedFilesAsync();
            foreach (var file in changedFiles)
            {
                Console.WriteLine($"  {file}");
            }
            Console.WriteLine();

            // Generate commit message with Claude.
            _logger.Info("Generating commit message...");
            var status = await _git.GetStatusAsync();
            var diff = await _git.GetDiffAsync();

            string message;
            try
            {
                message = await _messageGenerator.GenerateAsync(status, diff);
            }
            catch (Exception ex)
            {
                _logger.Error($"Error: Failed to generate commit message: {ex.Message}");
                _logger.Error("");
                _logger.Error("Make sure Claude CLI is installed: npm install -g @anthropic-ai/claude-code");
                return 1;
            }

            // Show generated message.
            Console.WriteLine();
            _logger.Info("Generated commit message:");
            Console.WriteLine("────────────────────────────────────────");
            Console.WriteLine(message);
            Console.WriteLine("────────────────────────────────────────");
            Console.WriteLine();

            // Confirm with user.
            Console.Write("Commit with this message? [Y/n] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (response == "n" || response == "no")
            {
                _logger.Info("Commit cancelled.");
                return 0;
            }

            // Stage all changes and commit.
            await _git.StageAllAsync();
            await _git.CommitAsync(message);

            Console.WriteLine();
            _logger.Info($"Committed: {message.Split('\n')[0]}");

            return 0;
        }
        catch (Exception ex)
        {
            _logger.Error($"Error: {ex.Message}");
            return 1;
        }
    }
}
