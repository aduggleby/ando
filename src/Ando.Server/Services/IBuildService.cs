// =============================================================================
// IBuildService.cs
//
// Summary: Interface for build management operations.
//
// Provides methods for queuing, retrieving, cancelling, and retrying builds.
// The service handles the business logic while the orchestrator handles
// actual build execution.
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.Services;

/// <summary>
/// Service for managing builds.
/// </summary>
public interface IBuildService
{
    /// <summary>
    /// Queues a new build for a project.
    /// </summary>
    /// <param name="projectId">Project to build.</param>
    /// <param name="commitSha">Commit SHA to build.</param>
    /// <param name="branch">Branch name.</param>
    /// <param name="trigger">What triggered the build.</param>
    /// <param name="commitMessage">Optional commit message.</param>
    /// <param name="commitAuthor">Optional commit author.</param>
    /// <param name="pullRequestNumber">Optional PR number if this is a PR build.</param>
    /// <returns>The new build ID.</returns>
    Task<int> QueueBuildAsync(
        int projectId,
        string commitSha,
        string branch,
        BuildTrigger trigger,
        string? commitMessage = null,
        string? commitAuthor = null,
        int? pullRequestNumber = null,
        string? profile = null);

    /// <summary>
    /// Gets a build by ID with related project data.
    /// </summary>
    Task<Build?> GetBuildAsync(int buildId);

    /// <summary>
    /// Gets builds for a project with pagination.
    /// </summary>
    Task<IReadOnlyList<Build>> GetBuildsForProjectAsync(int projectId, int skip = 0, int take = 20);

    /// <summary>
    /// Gets recent builds across all projects for a user.
    /// </summary>
    Task<IReadOnlyList<Build>> GetRecentBuildsForUserAsync(int userId, int take = 10);

    /// <summary>
    /// Cancels a running build.
    /// </summary>
    /// <returns>True if the build was cancelled.</returns>
    Task<bool> CancelBuildAsync(int buildId);

    /// <summary>
    /// Retries a failed or cancelled build.
    /// </summary>
    /// <returns>The new build ID.</returns>
    Task<int> RetryBuildAsync(int buildId);

    /// <summary>
    /// Updates build status and related fields.
    /// </summary>
    Task UpdateBuildStatusAsync(
        int buildId,
        BuildStatus status,
        string? errorMessage = null,
        int? stepsTotal = null,
        int? stepsCompleted = null,
        int? stepsFailed = null);
}
