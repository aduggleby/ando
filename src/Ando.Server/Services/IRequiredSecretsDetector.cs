// =============================================================================
// IRequiredSecretsDetector.cs
//
// Summary: Interface for detecting required environment variables from build scripts.
//
// Parses build.csando files to extract environment variable requirements,
// looking for Env() calls and similar patterns that indicate required secrets.
// =============================================================================

namespace Ando.Server.Services;

/// <summary>
/// Service for detecting required environment variables from build scripts.
/// </summary>
public interface IRequiredSecretsDetector
{
    /// <summary>
    /// Detects required environment variables from a repository's build script.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    /// <param name="repoFullName">Repository full name (owner/repo).</param>
    /// <param name="branch">Branch to read from (optional, uses default branch if null).</param>
    /// <returns>List of detected required environment variable names.</returns>
    Task<IReadOnlyList<string>> DetectRequiredSecretsAsync(long installationId, string repoFullName, string? branch = null);

    /// <summary>
    /// Detects required environment variables from build script content.
    /// </summary>
    /// <param name="scriptContent">The content of a build.csando file.</param>
    /// <returns>List of detected required environment variable names.</returns>
    IReadOnlyList<string> ParseRequiredSecrets(string scriptContent);
}
