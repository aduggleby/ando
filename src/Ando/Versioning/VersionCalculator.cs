// =============================================================================
// VersionCalculator.cs
//
// Summary: Computes and validates new versions for 'ando bump'.
//
// Supports the default semver scheme and the opt-in "year-minor-patch" scheme
// (YYYY.MINOR.PATCH), where the first number is the calendar year.
//
// Design Decisions:
// - The current date is passed in so year rollover is testable
// - Year scheme: a new year restarts at <year>.1.0 for minor and patch bumps
// - Major bumps are rejected in the year scheme (the year is the major)
// =============================================================================

using System.Text.RegularExpressions;

namespace Ando.Versioning;

public static partial class VersionCalculator
{
    public const string SemVerScheme = "semver";
    public const string YearScheme = "year-minor-patch";

    [GeneratedRegex(@"^(\d{4})\.\d+\.\d+$")]
    private static partial Regex YearVersionRegex();

    public static bool IsKnownScheme(string scheme) => scheme is SemVerScheme or YearScheme;

    /// <summary>Computes the next version for a bump type.</summary>
    public static string Next(string current, BumpType type, string scheme, DateTime utcNow)
    {
        var semver = SemVer.Parse(current);
        if (scheme != YearScheme)
            return semver.Bump(type).ToString();

        if (type == BumpType.Major)
            throw new ArgumentException(
                $"A major bump is not supported with versionScheme '{YearScheme}'. Use minor, patch or --set.");

        if (semver.Major < utcNow.Year)
            return $"{utcNow.Year}.1.0";

        return type == BumpType.Minor
            ? $"{semver.Major}.{semver.Minor + 1}.0"
            : $"{semver.Major}.{semver.Minor}.{semver.Patch + 1}";
    }

    /// <summary>Validates an explicit --set version and returns it normalized.</summary>
    public static string ValidateExplicit(string requested, string current, string scheme, DateTime utcNow)
    {
        if (scheme != YearScheme)
        {
            if (!SemVer.TryParse(requested, out var parsed))
                throw new ArgumentException($"Invalid version '{requested}'. Expected MAJOR.MINOR.PATCH.");
            return parsed.ToString();
        }

        var match = YearVersionRegex().Match(requested);
        if (!match.Success)
            throw new ArgumentException($"Invalid version '{requested}'. Expected YYYY.MINOR.PATCH.");

        var year = int.Parse(match.Groups[1].Value);
        if (year < 2000 || year > utcNow.Year + 1)
            throw new ArgumentException($"Implausible year {year} in version '{requested}'.");

        if (SemVer.Parse(requested).CompareTo(SemVer.Parse(current)) <= 0)
            throw new ArgumentException($"Version '{requested}' must be greater than the current version {current}.");

        return requested;
    }
}
