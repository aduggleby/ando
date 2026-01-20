// =============================================================================
// ProfileDetector.cs
//
// Summary: Detects available profiles from build scripts.
//
// Parses build.csando files to find DefineProfile() calls that indicate
// available build profiles. This enables automatic detection of what profiles
// a project supports before builds run.
//
// Patterns detected:
// - DefineProfile("profile-name")
// - var profileName = DefineProfile("profile-name")
//
// Design Decisions:
// - Uses regex for parsing (simpler than Roslyn for this use case)
// - Returns distinct, sorted list for consistency
// - Profile names are case-insensitive for matching
// =============================================================================

using System.Text.RegularExpressions;
using Ando.Server.GitHub;

namespace Ando.Server.Services;

/// <summary>
/// Detects available profiles from build scripts.
/// </summary>
public partial class ProfileDetector : IProfileDetector
{
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<ProfileDetector> _logger;

    // -------------------------------------------------------------------------
    // Regex patterns for detecting profile definitions
    // -------------------------------------------------------------------------

    // Matches: DefineProfile("profile-name") or DefineProfile("profile_name")
    // Profile names can contain letters, numbers, hyphens, and underscores
    [GeneratedRegex(@"DefineProfile\s*\(\s*""([A-Za-z][A-Za-z0-9_-]*)""\s*\)", RegexOptions.Compiled)]
    private static partial Regex DefineProfilePattern();

    public ProfileDetector(IGitHubService gitHubService, ILogger<ProfileDetector> logger)
    {
        _gitHubService = gitHubService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DetectProfilesAsync(
        long installationId,
        string repoFullName,
        string? branch = null)
    {
        var allProfiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Fetch main build.csando
        var mainContent = await _gitHubService.GetFileContentAsync(
            installationId,
            repoFullName,
            "build.csando",
            branch);

        if (string.IsNullOrEmpty(mainContent))
        {
            _logger.LogDebug("No build.csando found in {Repo}", repoFullName);
            return [];
        }

        // Parse main script
        var mainProfiles = ParseProfiles(mainContent);
        foreach (var profile in mainProfiles)
        {
            allProfiles.Add(profile);
        }

        _logger.LogInformation(
            "Detected {Count} profiles in {Repo}: {Profiles}",
            allProfiles.Count,
            repoFullName,
            allProfiles.Count > 0 ? string.Join(", ", allProfiles.Order()) : "(none)");

        return allProfiles.Order().ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ParseProfiles(string scriptContent)
    {
        var profiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find DefineProfile("name") calls
        var matches = DefineProfilePattern().Matches(scriptContent);
        foreach (Match match in matches)
        {
            var profileName = match.Groups[1].Value;
            profiles.Add(profileName);
        }

        return profiles.Order().ToList();
    }
}
