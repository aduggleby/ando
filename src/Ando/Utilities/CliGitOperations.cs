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
    /// Porcelain status, optionally ignoring ANDO's own build.csando.log and .ando/ files.
    /// </summary>
    public async Task<string> GetStatusAsync(bool excludeAndoFiles)
    {
        if (!excludeAndoFiles) return await GetStatusAsync();
        var result = await _runner.RunAsync("git",
            "status --porcelain -- . \":(exclude)build.csando.log\" \":(exclude).ando\"");
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

    // Quotes a value for the git argument string; refuses values that cannot be quoted safely
    // (the runner takes one argument string, so quotes, backslashes and newlines are rejected).
    private static string Quote(string value)
    {
        if (value.Contains('"') || value.Contains('\\') || value.Contains('\n'))
            throw new ArgumentException(
                $"Unsupported character (quote, backslash or newline) in git argument: {value}");
        return $"\"{value}\"";
    }

    /// <summary>
    /// Most recent tag reachable from HEAD matching the glob, or null when there is none.
    /// </summary>
    public async Task<string?> GetLastTagMatchingAsync(string pattern)
    {
        var result = await _runner.RunAsync("git", $"describe --tags --abbrev=0 --match {Quote(pattern)}");
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output) ? result.Output.Trim() : null;
    }

    /// <summary>
    /// Non-merge commits (newest first) as 7-character sha and subject, since the tag
    /// (all history when null), optionally limited to paths.
    /// </summary>
    public async Task<List<(string Sha, string Subject)>> GetCommitLogAsync(string? sinceTag, IEnumerable<string>? paths)
    {
        var range = sinceTag == null ? "HEAD" : $"{Quote(sinceTag)}..HEAD";
        var pathSpec = paths?.Select(Quote).ToList() is { Count: > 0 } list ? $" -- {string.Join(" ", list)}" : "";
        var result = await _runner.RunAsync("git", $"log {range} --no-merges --abbrev=7 --format=%h%x09%s{pathSpec}");
        if (result.ExitCode != 0)
            throw new Exception($"Git log failed: {result.Error}");

        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('\t', 2))
            .Where(parts => parts.Length == 2)
            .Select(parts => (parts[0][..Math.Min(7, parts[0].Length)], parts[1]))
            .ToList();
    }

    /// <summary>Commit a tag points at (peeled), or null when the tag does not exist.</summary>
    public async Task<string?> GetTagCommitAsync(string tag)
    {
        var result = await _runner.RunAsync("git", $"rev-parse -q --verify {Quote($"refs/tags/{tag}^{{commit}}")}");
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    public async Task<string?> GetHeadCommitAsync()
    {
        var result = await _runner.RunAsync("git", "rev-parse HEAD");
        return result.ExitCode == 0 ? result.Output.Trim() : null;
    }

    /// <summary>Stages exactly the given paths.</summary>
    public async Task StagePathsAsync(IEnumerable<string> paths)
    {
        var result = await _runner.RunAsync("git", $"add -- {string.Join(" ", paths.Select(Quote))}");
        if (result.ExitCode != 0)
            throw new Exception($"Git add failed: {result.Error}");
    }

    /// <summary>Commits only the given paths (never other staged or modified files).</summary>
    public async Task CommitPathsAsync(string message, IEnumerable<string> paths)
    {
        var result = await _runner.RunAsync("git", $"commit -F - -- {string.Join(" ", paths.Select(Quote))}", stdin: message);
        if (result.ExitCode != 0)
        {
            var error = !string.IsNullOrWhiteSpace(result.Error) ? result.Error : result.Output;
            throw new Exception($"Git commit failed: {error}");
        }
    }

    public async Task CreateAnnotatedTagAsync(string tag, string message)
    {
        var result = await _runner.RunAsync("git", $"tag -a {Quote(tag)} -F -", stdin: message);
        if (result.ExitCode != 0)
            throw new Exception($"Git tag failed: {result.Error}");
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
    /// Gets detailed commit information since a specific tag.
    /// Each entry includes hash, subject, and list of changed files.
    /// </summary>
    /// <param name="tag">The tag to get commits since (e.g., "v1.0.0").</param>
    /// <returns>List of commit details.</returns>
    public async Task<List<CommitInfo>> GetDetailedCommitsSinceTagAsync(string tag)
    {
        // Get commits with hash and subject, separated by a delimiter.
        var result = await _runner.RunAsync("git", $"log {tag}..HEAD --pretty=format:%H|%s");
        if (result.ExitCode != 0)
            return [];

        var commits = new List<CommitInfo>();
        var lines = result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var line in lines)
        {
            var parts = line.Split('|', 2);
            if (parts.Length < 2) continue;

            var hash = parts[0].Trim();
            var subject = parts[1].Trim();

            // Get files changed in this commit.
            var filesResult = await _runner.RunAsync("git", $"diff-tree --no-commit-id --name-only -r {hash}");
            var files = filesResult.ExitCode == 0
                ? filesResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList()
                : [];

            commits.Add(new CommitInfo(hash, subject, files));
        }

        return commits;
    }

    /// <summary>
    /// Gets all commits since the last tag (or all commits if no tags).
    /// Returns detailed commit information including changed files.
    /// </summary>
    public async Task<(List<CommitInfo> Commits, string? SinceTag)> GetCommitsSinceLastTagAsync()
    {
        var lastTag = await GetLastTagAsync();
        if (lastTag == null)
        {
            // No tags - get recent commits (limit to 50).
            var result = await _runner.RunAsync("git", "log -50 --pretty=format:%H|%s");
            if (result.ExitCode != 0)
                return ([], null);

            var commits = new List<CommitInfo>();
            foreach (var line in result.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('|', 2);
                if (parts.Length < 2) continue;

                var hash = parts[0].Trim();
                var subject = parts[1].Trim();

                var filesResult = await _runner.RunAsync("git", $"diff-tree --no-commit-id --name-only -r {hash}");
                var files = filesResult.ExitCode == 0
                    ? filesResult.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList()
                    : [];

                commits.Add(new CommitInfo(hash, subject, files));
            }

            return (commits, null);
        }

        return (await GetDetailedCommitsSinceTagAsync(lastTag), lastTag);
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

/// <summary>
/// Represents detailed information about a git commit.
/// </summary>
/// <param name="Hash">The full commit hash.</param>
/// <param name="Subject">The commit subject (first line of message).</param>
/// <param name="Files">List of files changed in this commit.</param>
public record CommitInfo(string Hash, string Subject, List<string> Files);
