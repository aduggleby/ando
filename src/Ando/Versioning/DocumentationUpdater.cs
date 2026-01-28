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
    /// Updates version badges in documentation files.
    /// Changelog updates are handled separately by Claude.
    /// </summary>
    /// <param name="oldVersion">Previous version string.</param>
    /// <param name="newVersion">New version string.</param>
    /// <returns>List of update results.</returns>
    public List<UpdateResult> UpdateVersionBadges(string oldVersion, string newVersion)
    {
        var results = new List<UpdateResult>();

        // Update version badge.
        var badgePath = FindVersionBadge();
        if (badgePath != null)
        {
            results.Add(UpdateVersionBadge(badgePath, oldVersion, newVersion));
        }

        return results;
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
