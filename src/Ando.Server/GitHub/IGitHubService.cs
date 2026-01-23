// =============================================================================
// IGitHubService.cs
//
// Summary: Interface for GitHub API operations.
//
// Provides methods for interacting with the GitHub API including commit status
// updates, repository cloning, and user/repo information retrieval.
// =============================================================================

namespace Ando.Server.GitHub;

/// <summary>
/// Commit status state as defined by GitHub API.
/// </summary>
public enum CommitStatusState
{
    Pending,
    Success,
    Failure,
    Error
}

/// <summary>
/// Service for interacting with the GitHub API.
/// </summary>
public interface IGitHubService
{
    /// <summary>
    /// Posts a commit status to GitHub.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    /// <param name="repoFullName">Repository full name (owner/repo).</param>
    /// <param name="commitSha">Commit SHA to update.</param>
    /// <param name="state">Status state (pending, success, failure, error).</param>
    /// <param name="description">Optional description.</param>
    /// <param name="targetUrl">Optional URL to link to.</param>
    /// <param name="context">Status context (e.g., "ci/ando").</param>
    Task<bool> SetCommitStatusAsync(
        long installationId,
        string repoFullName,
        string commitSha,
        CommitStatusState state,
        string? description = null,
        string? targetUrl = null,
        string context = "ci/ando");

    /// <summary>
    /// Clones a repository to the specified directory.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    /// <param name="repoFullName">Repository full name (owner/repo).</param>
    /// <param name="branch">Branch to clone.</param>
    /// <param name="commitSha">Specific commit to checkout.</param>
    /// <param name="targetDirectory">Directory to clone into.</param>
    Task<bool> CloneRepositoryAsync(
        long installationId,
        string repoFullName,
        string branch,
        string commitSha,
        string targetDirectory);

    /// <summary>
    /// Fetches latest changes for an already cloned repository.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    /// <param name="repoFullName">Repository full name (owner/repo).</param>
    /// <param name="branch">Branch to fetch.</param>
    /// <param name="commitSha">Specific commit to checkout.</param>
    /// <param name="repoDirectory">Directory containing the repository.</param>
    Task<bool> FetchAndCheckoutAsync(
        long installationId,
        string repoFullName,
        string branch,
        string commitSha,
        string repoDirectory);

    /// <summary>
    /// Gets an installation access token for API calls.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    Task<string?> GetInstallationTokenAsync(long installationId);

    /// <summary>
    /// Gets repositories accessible to an installation.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    Task<IReadOnlyList<GitHubRepository>> GetInstallationRepositoriesAsync(long installationId);

    /// <summary>
    /// Gets repositories accessible to a user via their access token.
    /// </summary>
    /// <param name="accessToken">User's OAuth access token.</param>
    Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(string accessToken);

    /// <summary>
    /// Gets the GitHub App installation ID for a repository.
    /// Uses the App's JWT to authenticate.
    /// </summary>
    /// <param name="repoFullName">Repository full name (owner/repo).</param>
    /// <returns>Installation ID and repository info, or null if not installed.</returns>
    Task<(long InstallationId, GitHubRepository Repository)?> GetRepositoryInstallationAsync(string repoFullName);

    /// <summary>
    /// Gets the latest commit SHA for a branch.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    /// <param name="repoFullName">Repository full name (owner/repo).</param>
    /// <param name="branch">Branch name.</param>
    /// <returns>Commit SHA, or null if not found.</returns>
    Task<string?> GetBranchHeadShaAsync(long installationId, string repoFullName, string branch);

    /// <summary>
    /// Gets the content of a file from a repository.
    /// </summary>
    /// <param name="installationId">GitHub App installation ID.</param>
    /// <param name="repoFullName">Repository full name (owner/repo).</param>
    /// <param name="filePath">Path to the file within the repository.</param>
    /// <param name="branch">Branch or commit ref (optional, defaults to default branch).</param>
    /// <returns>File content, or null if not found.</returns>
    Task<string?> GetFileContentAsync(long installationId, string repoFullName, string filePath, string? branch = null);
}

/// <summary>
/// Repository information from GitHub API.
/// </summary>
public record GitHubRepository(
    long Id,
    string FullName,
    string HtmlUrl,
    string CloneUrl,
    string DefaultBranch,
    bool IsPrivate,
    long OwnerId,
    string OwnerLogin
);
