// =============================================================================
// CommitMessageGenerator.cs
//
// Summary: Generates commit messages using Claude CLI.
//
// This class sends git diff and status information to Claude to generate
// conventional commit messages. It handles large diffs by truncation and
// cleans the response to remove any markdown formatting.
//
// Design Decisions:
// - Uses Claude CLI with --dangerously-skip-permissions for non-interactive use
// - Truncates large diffs (>8000 chars) to stay within reasonable context
// - Cleans response to remove quotes and markdown code blocks
// - Follows conventional commit format: type(scope): description
// =============================================================================

using Ando.Utilities;

namespace Ando.AI;

/// <summary>
/// Generates git commit messages using Claude AI.
/// </summary>
public class CommitMessageGenerator
{
    private const int MaxDiffLength = 8000;
    private readonly CliProcessRunner _runner;

    public CommitMessageGenerator(CliProcessRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Generates a commit message based on git status and diff.
    /// </summary>
    /// <param name="gitStatus">Output from 'git status --porcelain'.</param>
    /// <param name="gitDiff">Output from 'git diff'.</param>
    /// <returns>Generated commit message.</returns>
    public async Task<string> GenerateAsync(string gitStatus, string gitDiff)
    {
        var prompt = BuildPrompt(gitStatus, gitDiff);
        var response = await _runner.RunClaudeAsync(prompt);
        return CleanResponse(response);
    }

    /// <summary>
    /// Builds the prompt for Claude including rules and context.
    /// </summary>
    private string BuildPrompt(string status, string diff)
    {
        var truncatedDiff = TruncateDiff(diff, MaxDiffLength);

        return $"""
            Generate a concise git commit message for the following changes.

            Rules:
            - Use conventional commit format: type(scope): description
            - Types: feat, fix, docs, style, refactor, test, chore, build
            - First line max 72 characters
            - Be specific but concise
            - Focus on WHAT changed and WHY, not HOW
            - If multiple unrelated changes, focus on the primary change

            Files changed:
            {status}

            Diff:
            {truncatedDiff}

            Output ONLY the commit message. No markdown, no explanations, no quotes.
            """;
    }

    /// <summary>
    /// Truncates diff if it exceeds the maximum length.
    /// </summary>
    private static string TruncateDiff(string diff, int maxLength)
    {
        // Diff is already filtered by CliGitOperations.GetDiffAsync using git pathspec.
        if (diff.Length <= maxLength)
            return diff;

        return diff[..maxLength] + "\n\n[Diff truncated - showing first 8000 characters]";
    }

    /// <summary>
    /// Cleans Claude's response by removing any formatting artifacts.
    /// </summary>
    private static string CleanResponse(string response)
    {
        var clean = response.Trim();

        // Remove quotes if wrapped in them.
        if ((clean.StartsWith('"') && clean.EndsWith('"')) ||
            (clean.StartsWith('\'') && clean.EndsWith('\'')))
        {
            clean = clean[1..^1];
        }

        // Remove markdown code blocks if present.
        if (clean.StartsWith("```"))
        {
            var lines = clean.Split('\n');
            clean = string.Join('\n', lines.Skip(1).TakeWhile(l => !l.StartsWith("```")));
        }

        return clean.Trim();
    }
}
