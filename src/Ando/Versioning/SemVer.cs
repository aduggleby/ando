// =============================================================================
// SemVer.cs
//
// Summary: Semantic version parsing and manipulation.
//
// This record represents a semantic version (major.minor.patch-prerelease)
// and provides parsing, validation, and bumping operations.
//
// Design Decisions:
// - Uses record type for immutability and value equality
// - TryParse pattern for safe parsing without exceptions
// - Bump method returns new instance (immutable)
// - Prerelease is cleared on bump (1.0.0-beta -> 1.0.0 on patch)
// =============================================================================

using System.Text.RegularExpressions;

namespace Ando.Versioning;

/// <summary>
/// Type of version bump to perform.
/// </summary>
public enum BumpType
{
    Patch,
    Minor,
    Major
}

/// <summary>
/// Represents a semantic version with optional prerelease suffix.
/// Follows semver.org specification for versioning.
/// </summary>
/// <param name="Major">Major version (breaking changes).</param>
/// <param name="Minor">Minor version (new features, backwards compatible).</param>
/// <param name="Patch">Patch version (bug fixes, backwards compatible).</param>
/// <param name="Prerelease">Optional prerelease identifier (e.g., "beta.1").</param>
public partial record SemVer(int Major, int Minor, int Patch, string? Prerelease = null)
{
    // Regex pattern for parsing semantic versions.
    // Matches: 1.2.3, 1.2.3-beta, 1.2.3-beta.1, etc.
    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)(?:-(.+))?$", RegexOptions.Compiled)]
    private static partial Regex VersionRegex();

    /// <summary>
    /// Parses a version string into a SemVer instance.
    /// </summary>
    /// <param name="version">Version string (e.g., "1.2.3" or "1.2.3-beta").</param>
    /// <returns>Parsed SemVer instance.</returns>
    /// <exception cref="ArgumentException">Thrown if version format is invalid.</exception>
    public static SemVer Parse(string version)
    {
        if (!TryParse(version, out var result))
            throw new ArgumentException($"Invalid version format: {version}");
        return result;
    }

    /// <summary>
    /// Attempts to parse a version string into a SemVer instance.
    /// </summary>
    /// <param name="version">Version string to parse.</param>
    /// <param name="result">Parsed SemVer if successful.</param>
    /// <returns>True if parsing succeeded, false otherwise.</returns>
    public static bool TryParse(string version, out SemVer result)
    {
        result = default!;

        if (string.IsNullOrWhiteSpace(version))
            return false;

        // Remove leading 'v' if present (e.g., "v1.2.3" -> "1.2.3").
        var versionStr = version.Trim();
        if (versionStr.StartsWith('v') || versionStr.StartsWith('V'))
            versionStr = versionStr[1..];

        var match = VersionRegex().Match(versionStr);
        if (!match.Success)
            return false;

        result = new SemVer(
            int.Parse(match.Groups[1].Value),
            int.Parse(match.Groups[2].Value),
            int.Parse(match.Groups[3].Value),
            match.Groups[4].Success ? match.Groups[4].Value : null
        );
        return true;
    }

    /// <summary>
    /// Creates a new SemVer with the specified bump type applied.
    /// Bumping clears the prerelease suffix.
    /// </summary>
    /// <param name="type">Type of bump to apply.</param>
    /// <returns>New SemVer with bumped version.</returns>
    public SemVer Bump(BumpType type)
    {
        return type switch
        {
            BumpType.Patch => this with { Patch = Patch + 1, Prerelease = null },
            BumpType.Minor => this with { Minor = Minor + 1, Patch = 0, Prerelease = null },
            BumpType.Major => this with { Major = Major + 1, Minor = 0, Patch = 0, Prerelease = null },
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
    }

    /// <summary>
    /// Converts the version to its string representation.
    /// </summary>
    public override string ToString()
        => Prerelease is null
            ? $"{Major}.{Minor}.{Patch}"
            : $"{Major}.{Minor}.{Patch}-{Prerelease}";
}
