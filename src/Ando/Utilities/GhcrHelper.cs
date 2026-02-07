// =============================================================================
// GhcrHelper.cs
//
// Summary: Shared helper for GitHub Container Registry (ghcr.io) operations.
//
// Provides common functionality for authenticating to ghcr.io, used by both
// DockerOperations.Buildx and GitHubOperations.PushImage.
//
// Design Decisions:
// - Extracts owner from ghcr.io tags to work in containers without .git
// - Centralizes login logic to keep operations DRY
// - Static methods for easy use across different operation classes
// =============================================================================

using Ando.Execution;
using Ando.Logging;

namespace Ando.Utilities;

/// <summary>
/// Helper for GitHub Container Registry (ghcr.io) operations.
/// </summary>
public static class GhcrHelper
{
    /// <summary>
    /// Logs in to GitHub Container Registry using the provided token and owner.
    /// </summary>
    /// <param name="executor">Command executor to run docker login.</param>
    /// <param name="logger">Logger for output.</param>
    /// <param name="token">GitHub token with packages:write scope.</param>
    /// <param name="owner">GitHub owner (user or organization).</param>
    /// <returns>True if login succeeded, false otherwise.</returns>
    public static async Task<bool> LoginAsync(
        ICommandExecutor executor,
        IBuildLogger logger,
        string token,
        string owner)
    {
        logger.Info("Logging in to ghcr.io...");
        // Avoid embedding the token in the command line (visible in process lists).
        // Use env vars so only the child process inherits the secret.
        async Task<CommandResult> TryLoginAsync(string username)
        {
            var options = new CommandOptions();
            options.Environment["GITHUB_TOKEN"] = token;
            options.Environment["GHCR_USERNAME"] = username;
            return await executor.ExecuteAsync(
                "bash",
                ["-c", "echo \"$GITHUB_TOKEN\" | docker login ghcr.io -u \"$GHCR_USERNAME\" --password-stdin"],
                options);
        }

        // First try using the owner (works for PATs and typical docs).
        // If that fails (e.g. when using installation tokens), fall back to x-access-token.
        var loginResult = await TryLoginAsync(owner);
        if (!loginResult.Success)
        {
            logger.Warning("Docker login to ghcr.io failed with owner username; retrying with x-access-token");
            loginResult = await TryLoginAsync("x-access-token");
        }

        if (!loginResult.Success)
        {
            logger.Error($"Docker login to ghcr.io failed: {loginResult.Error}");
            return false;
        }

        return true;
    }

    /// <summary>
    /// Extracts the GitHub owner from a ghcr.io image reference.
    /// </summary>
    /// <param name="imageRef">Image reference (e.g., "ghcr.io/owner/image:tag").</param>
    /// <returns>The owner, or null if not a valid ghcr.io reference.</returns>
    public static string? ExtractOwnerFromTag(string imageRef)
    {
        if (!imageRef.StartsWith("ghcr.io/"))
            return null;

        // Remove "ghcr.io/" prefix and split by "/".
        var path = imageRef["ghcr.io/".Length..];
        var parts = path.Split('/');

        // First part is the owner.
        return parts.Length >= 1 ? parts[0] : null;
    }

    /// <summary>
    /// Extracts the GitHub owner from a list of image tags.
    /// Returns the owner from the first ghcr.io tag found.
    /// </summary>
    /// <param name="tags">List of image tags.</param>
    /// <returns>The owner, or null if no ghcr.io tags found.</returns>
    public static string? ExtractOwnerFromTags(IEnumerable<string> tags)
    {
        foreach (var tag in tags)
        {
            var owner = ExtractOwnerFromTag(tag);
            if (owner != null)
                return owner;
        }
        return null;
    }
}
