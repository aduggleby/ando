// =============================================================================
// CliGitOperations.cs
//
// Summary: Git operations for CLI commands (bump, commit, release).
//
// This class provides direct git operations with output capture. Unlike the
// build system's GitOperations which registers steps in a workflow, this
// executes commands immediately and returns results.
//
// Design Decisions:
// - Uses git pathspec for file exclusions (cleaner than manual filtering)
// - Handles git status parsing including renames ("R old -> new" format)
// - Methods are async for consistency with process execution
// - Throws exceptions on critical failures (e.g., commit fails)
// =============================================================================

using System.Text;

namespace Ando.Utilities;

/// <summary>
/// Git operations for CLI commands. Executes git commands directly
/// and returns results for programmatic use.
/// </summary>
public class CliGitOperations
{
    private readonly CliProcessRunner _runner;

    public CliGitOperations(CliProcessRunner runner)
    {
        _runner = runner;
    }

    /// <summary>
    /// Checks if there are uncommitted changes (staged or unstaged).
    /// </summary>
    public async Task<bool> HasUncommittedChangesAsync()
    {
        var result = await _runner.RunAsync("git", "status --porcelain");
        return !string.IsNullOrWhiteSpace(result.Output);
    }

    /// <summary>
    /// Gets the raw git status output (porcelain format).
    /// </summary>
    public async Task<string> GetStatusAsync()
    {
        var result = await _runner.RunAsync("git", "status --porcelain");
        return result.Output;
    }

    /// <summary>
    /// Gets a list of changed files (staged and unstaged).
    /// </summary>
    public async Task<List<string>> GetChangedFilesAsync()
    {
        var status = await GetStatusAsync();
        return status
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line =>
            {
                // Handle renames: "R  old -> new" - return the new name.
                if (line.Contains(" -> "))
                    return line.Split(" -> ").Last().Trim();
                // Standard format: "XY filename" where XY is the status.
                return line.Length > 3 ? line[3..].Trim() : line.Trim();
            })
            .Where(f => !string.IsNullOrEmpty(f))
            .ToList();
    }

    /// <summary>
    /// Gets the diff of staged and unstaged changes, excluding noisy files.
    /// Uses git pathspec to exclude lock files and minified assets.
    /// </summary>
    /// <param name="includeUntracked">Whether to include untracked files in diff.</param>
    public async Task<string> GetDiffAsync(bool includeUntracked = false)
    {
        // Git pathspec exclusions for noisy files.
        var excludes = string.Join(" ",
            ":(exclude)package-lock.json",
            ":(exclude)yarn.lock",
            ":(exclude)pnpm-lock.yaml",
            ":(exclude)*.min.js",
            ":(exclude)*.min.css");

        var staged = await _runner.RunAsync("git", $"diff --cached -- . {excludes}");
        var unstaged = await _runner.RunAsync("git", $"diff -- . {excludes}");

        var result = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(staged.Output))
            result.AppendLine(staged.Output);
        if (!string.IsNullOrWhiteSpace(unstaged.Output))
            result.AppendLine(unstaged.Output);

        return result.ToString();
    }

    /// <summary>
    /// Gets the most recent tag, or null if no tags exist.
    /// </summary>
    public async Task<string?> GetLastTagAsync()
    {
        var result = await _runner.RunAsync("git", "describe --tags --abbrev=0");
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Gets the diff between a tag and HEAD.
    /// </summary>
    public async Task<string> GetDiffSinceTagAsync(string tag)
    {
        var result = await _runner.RunAsync("git", $"diff {tag}..HEAD");
        return result.Output;
    }

    /// <summary>
    /// Stages all changes (git add -A).
    /// </summary>
    public async Task StageAllAsync()
    {
        await _runner.RunAsync("git", "add -A");
    }

    /// <summary>
    /// Stages specific files.
    /// </summary>
    public async Task StageFilesAsync(IEnumerable<string> files)
    {
        var fileList = string.Join(" ", files.Select(f => $"\"{f}\""));
        await _runner.RunAsync("git", $"add {fileList}");
    }

    /// <summary>
    /// Creates a commit with the specified message.
    /// Uses -F - to read message from stdin (handles multi-line messages safely).
    /// </summary>
    /// <exception cref="Exception">Thrown if commit fails.</exception>
    public async Task CommitAsync(string message)
    {
        var result = await _runner.RunAsync("git", "commit -F -", stdin: message);
        if (result.ExitCode != 0)
        {
            var error = !string.IsNullOrWhiteSpace(result.Error)
                ? result.Error
                : result.Output;
            throw new Exception($"Git commit failed: {error}");
        }
    }

    /// <summary>
    /// Gets the current branch name.
    /// </summary>
    public async Task<string> GetCurrentBranchAsync()
    {
        var result = await _runner.RunAsync("git", "branch --show-current");
        return result.Output.Trim();
    }

    /// <summary>
    /// Checks if the current branch has a remote tracking branch.
    /// </summary>
    public async Task<bool> HasRemoteTrackingAsync()
    {
        var result = await _runner.RunAsync("git", "rev-parse --abbrev-ref @{upstream}");
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Gets the remote tracking branch name, or null if none.
    /// </summary>
    public async Task<string?> GetRemoteTrackingBranchAsync()
    {
        var result = await _runner.RunAsync("git", "rev-parse --abbrev-ref @{upstream}");
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Gets the short commit hash of HEAD.
    /// </summary>
    public async Task<string> GetCurrentCommitShortAsync()
    {
        var result = await _runner.RunAsync("git", "rev-parse --short HEAD");
        return result.Output.Trim();
    }

    /// <summary>
    /// Pushes to the remote.
    /// </summary>
    /// <exception cref="Exception">Thrown if push fails.</exception>
    public async Task PushAsync(bool streamOutput = false)
    {
        var result = await _runner.RunAsync("git", "push", timeoutMs: 120000, streamOutput: streamOutput);
        if (result.ExitCode != 0)
        {
            var error = !string.IsNullOrWhiteSpace(result.Error)
                ? result.Error
                : result.Output;
            throw new Exception($"Git push failed: {error}");
        }
    }

    /// <summary>
    /// Checks if we're in a git repository.
    /// </summary>
    public async Task<bool> IsGitRepositoryAsync()
    {
        var result = await _runner.RunAsync("git", "rev-parse --git-dir");
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Gets commit messages since a specific tag.
    /// Returns empty list if the tag doesn't exist.
    /// </summary>
    /// <param name="tag">The tag to get commits since (e.g., "v1.0.0").</param>
    /// <returns>List of commit messages (subject lines only).</returns>
    public async Task<List<string>> GetCommitMessagesSinceTagAsync(string tag)
    {
        var result = await _runner.RunAsync("git", $"log {tag}..HEAD --pretty=format:%s");
        if (result.ExitCode != 0)
            return [];

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    /// <summary>
    /// Checks if a tag exists.
    /// </summary>
    /// <param name="tag">The tag name to check.</param>
    public async Task<bool> TagExistsAsync(string tag)
    {
        var result = await _runner.RunAsync("git", $"rev-parse {tag}");
        return result.ExitCode == 0;
    }

    /// <summary>
    /// Gets the list of files changed since a specific tag.
    /// </summary>
    /// <param name="tag">The tag to compare against.</param>
    /// <returns>List of changed file paths.</returns>
    public async Task<List<string>> GetChangedFilesSinceTagAsync(string tag)
    {
        var result = await _runner.RunAsync("git", $"diff --name-only {tag}..HEAD");
        if (result.ExitCode != 0)
            return [];

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    /// <summary>
    /// Checks if there are any commits since the last tag.
    /// Returns true if no tags exist (first release) or if there are commits since the last tag.
    /// </summary>
    public async Task<(bool HasChanges, string? LastTag, int CommitCount)> GetChangesSinceLastTagAsync()
    {
        var lastTag = await GetLastTagAsync();
        if (lastTag == null)
        {
            // No tags exist - this is the first release.
            return (true, null, -1);
        }

        var commits = await GetCommitMessagesSinceTagAsync(lastTag);
        return (commits.Count > 0, lastTag, commits.Count);
    }
}
