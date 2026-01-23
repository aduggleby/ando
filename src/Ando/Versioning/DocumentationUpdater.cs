// =============================================================================
// DocumentationUpdater.cs
//
// Summary: Updates documentation files with new version information.
//
// This class handles updating changelog entries and version badges in
// documentation files. It searches for files in standard locations and
// applies updates while preserving formatting.
//
// Design Decisions:
// - Searches multiple standard locations for each file type
// - Returns UpdateResult for each file (success/failure with reason)
// - Handles YAML frontmatter in markdown files
// - Uses specific patterns for version badges to avoid false replacements
// =============================================================================

using System.Text.RegularExpressions;

namespace Ando.Versioning;

/// <summary>
/// Updates documentation files with new version information.
/// </summary>
public partial class DocumentationUpdater
{
    private readonly string _repoRoot;

    public DocumentationUpdater(string repoRoot)
    {
        _repoRoot = repoRoot;
    }

    /// <summary>
    /// Result of a documentation update operation.
    /// </summary>
    /// <param name="FilePath">Path to the file (relative to repo root).</param>
    /// <param name="Success">Whether the update succeeded.</param>
    /// <param name="Error">Error message if update failed.</param>
    public record UpdateResult(string FilePath, bool Success, string? Error = null);

    /// <summary>
    /// Updates all documentation files with new version information.
    /// </summary>
    /// <param name="oldVersion">Previous version string.</param>
    /// <param name="newVersion">New version string.</param>
    /// <param name="commitMessages">Optional list of commit messages to include in changelog.</param>
    /// <returns>List of update results.</returns>
    public List<UpdateResult> UpdateDocumentation(string oldVersion, string newVersion, IReadOnlyList<string>? commitMessages = null)
    {
        var results = new List<UpdateResult>();

        // Update changelog.
        var changelogPath = FindChangelog();
        if (changelogPath != null)
        {
            results.Add(UpdateChangelog(changelogPath, newVersion, commitMessages));
        }

        // Update version badge.
        var badgePath = FindVersionBadge();
        if (badgePath != null)
        {
            results.Add(UpdateVersionBadge(badgePath, oldVersion, newVersion));
        }

        return results;
    }

    /// <summary>
    /// Finds the changelog file in standard locations.
    /// </summary>
    private string? FindChangelog()
    {
        var candidates = new[]
        {
            "CHANGELOG.md",
            "changelog.md",
            "website/src/content/pages/changelog.md"
        };

        return candidates
            .Select(c => Path.Combine(_repoRoot, c))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Finds a file containing version badge in standard locations.
    /// </summary>
    private string? FindVersionBadge()
    {
        var candidates = new[]
        {
            "website/src/pages/index.astro",
            "README.md"
        };

        return candidates
            .Select(c => Path.Combine(_repoRoot, c))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>
    /// Updates the changelog with a new version entry.
    /// </summary>
    /// <param name="path">Path to the changelog file.</param>
    /// <param name="newVersion">New version string.</param>
    /// <param name="commitMessages">Optional list of commit messages to include.</param>
    private UpdateResult UpdateChangelog(string path, string newVersion, IReadOnlyList<string>? commitMessages)
    {
        try
        {
            var content = File.ReadAllText(path);
            var date = DateTime.Now.ToString("yyyy-MM-dd");

            // Find insertion point (after YAML frontmatter if present).
            var insertIndex = FindFrontmatterEnd(content);

            // Build changelog entries from commit messages or use default.
            var entriesText = BuildChangelogEntries(commitMessages);

            var entry = $"\n## {newVersion}\n\n**{date}**\n\n{entriesText}\n";
            var updated = content.Insert(insertIndex, entry);

            File.WriteAllText(path, updated);

            var relativePath = Path.GetRelativePath(_repoRoot, path);
            return new UpdateResult(relativePath, true);
        }
        catch (Exception ex)
        {
            var relativePath = Path.GetRelativePath(_repoRoot, path);
            return new UpdateResult(relativePath, false, ex.Message);
        }
    }

    /// <summary>
    /// Builds changelog entry text from commit messages.
    /// Filters out version bump commits and formats the rest as bullet points.
    /// </summary>
    private static string BuildChangelogEntries(IReadOnlyList<string>? commitMessages)
    {
        if (commitMessages == null || commitMessages.Count == 0)
        {
            return "- Version bump";
        }

        // Filter out version bump commits and empty messages.
        var relevantCommits = commitMessages
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .Where(m => !m.StartsWith("Bump version", StringComparison.OrdinalIgnoreCase))
            .Where(m => !Regex.IsMatch(m, @"^v?\d+\.\d+\.\d+$")) // Skip pure version number commits
            .ToList();

        if (relevantCommits.Count == 0)
        {
            return "- Version bump";
        }

        // Format each commit as a bullet point.
        // If commit follows conventional commit format (type: message), keep it.
        // Otherwise, just use the message as-is.
        var entries = relevantCommits.Select(m => $"- {m.Trim()}");
        return string.Join("\n", entries);
    }

    /// <summary>
    /// Finds the end position of YAML frontmatter in a markdown file.
    /// Returns 0 if no frontmatter is found.
    /// </summary>
    private static int FindFrontmatterEnd(string content)
    {
        if (!content.StartsWith("---"))
            return 0;

        // Find closing "---" on its own line (after the opening one).
        var lines = content.Split('\n');
        var charCount = lines[0].Length + 1; // First "---" line plus newline.

        for (var i = 1; i < lines.Length; i++)
        {
            // Check if this line is just "---" (possibly with trailing whitespace).
            if (lines[i].TrimEnd() == "---")
                return charCount + lines[i].Length + 1;

            charCount += lines[i].Length + 1;
        }

        return 0; // No closing frontmatter found, insert at start.
    }

    // Regex for version in quotes.
    [GeneratedRegex(@"([""'])v?(\d+\.\d+\.\d+)\1")]
    private static partial Regex QuotedVersionRegex();

    // Regex for version badge pattern (v0.9.23 at word boundary).
    [GeneratedRegex(@"\bv(\d+\.\d+\.\d+)\b(?!/)")]
    private static partial Regex VersionBadgeRegex();

    /// <summary>
    /// Updates version badge in a file.
    /// </summary>
    private UpdateResult UpdateVersionBadge(string path, string oldVersion, string newVersion)
    {
        try
        {
            var content = File.ReadAllText(path);
            var updated = content;

            // Replace version in quotes: "0.9.23" or '0.9.23'.
            updated = QuotedVersionRegex().Replace(updated, match =>
            {
                var quote = match.Groups[1].Value;
                var version = match.Groups[2].Value;
                if (version == oldVersion)
                    return $"{quote}{newVersion}{quote}";
                return match.Value;
            });

            // Replace version badge: v0.9.23.
            updated = VersionBadgeRegex().Replace(updated, match =>
            {
                var version = match.Groups[1].Value;
                if (version == oldVersion)
                    return $"v{newVersion}";
                return match.Value;
            });

            if (updated == content)
            {
                var relativePath = Path.GetRelativePath(_repoRoot, path);
                return new UpdateResult(relativePath, false, "No version pattern found to update");
            }

            File.WriteAllText(path, updated);

            var relPath = Path.GetRelativePath(_repoRoot, path);
            return new UpdateResult(relPath, true);
        }
        catch (Exception ex)
        {
            var relativePath = Path.GetRelativePath(_repoRoot, path);
            return new UpdateResult(relativePath, false, ex.Message);
        }
    }
}
