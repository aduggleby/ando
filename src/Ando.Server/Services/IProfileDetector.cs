// =============================================================================
// IProfileDetector.cs
//
// Summary: Interface for detecting available profiles from build scripts.
//
// Parses build.csando files to extract profile definitions via DefineProfile()
// calls. This enables automatic detection of what profiles a project supports.
// =============================================================================

namespace Ando.Server.Services;

/// <summary>
/// Service for detecting available profiles from build scripts.
/// </summary>
public interface IProfileDetector
{
    /// <summary>
    /// Detects available profiles from a repository's build script.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    /// <param name="repoFullName">Repository full name (owner/repo).</param>
    /// <param name="branch">Branch to read from (optional, uses default branch if null).</param>
    /// <returns>List of detected profile names.</returns>
    Task<IReadOnlyList<string>> DetectProfilesAsync(long installationId, string repoFullName, string? branch = null);

    /// <summary>
    /// Detects available profiles from build script content.
    /// </summary>
    /// <param name="scriptContent">The content of a build.csando file.</param>
    /// <returns>List of detected profile names.</returns>
    IReadOnlyList<string> ParseProfiles(string scriptContent);
}
