// =============================================================================
// IProjectService.cs
//
// Summary: Interface for project management operations.
//
// Provides methods for creating, updating, and retrieving projects.
// Handles project settings including secrets and notification configuration.
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.Services;

/// <summary>
/// Service for managing projects.
/// </summary>
public interface IProjectService
{
    /// <summary>
    /// Gets all projects for a user.
    /// </summary>
    Task<IReadOnlyList<Project>> GetProjectsForUserAsync(int userId);

    /// <summary>
    /// Gets a project by ID.
    /// </summary>
    Task<Project?> GetProjectAsync(int projectId);

    /// <summary>
    /// Gets a project by ID, verifying ownership.
    /// </summary>
    Task<Project?> GetProjectForUserAsync(int projectId, int userId);

    /// <summary>
    /// Creates a new project.
    /// </summary>
    Task<Project> CreateProjectAsync(
        int ownerId,
        long gitHubRepoId,
        string repoFullName,
        string repoUrl,
        string defaultBranch,
        long? installationId = null);

    /// <summary>
    /// Updates project settings.
    /// Note: RequiredSecrets and AvailableProfiles are auto-detected from build.csando and not set manually.
    /// </summary>
    Task<bool> UpdateProjectSettingsAsync(
        int projectId,
        string branchFilter,
        bool enablePrBuilds,
        int timeoutMinutes,
        string? dockerImage,
        string? profile,
        bool notifyOnFailure,
        string? notificationEmail);

    /// <summary>
    /// Deletes a project and all associated data.
    /// </summary>
    Task<bool> DeleteProjectAsync(int projectId);

    /// <summary>
    /// Sets or updates a secret for a project.
    /// </summary>
    Task SetSecretAsync(int projectId, string name, string value);

    /// <summary>
    /// Deletes a secret from a project.
    /// </summary>
    Task<bool> DeleteSecretAsync(int projectId, string name);

    /// <summary>
    /// Gets secret names (not values) for a project.
    /// </summary>
    Task<IReadOnlyList<string>> GetSecretNamesAsync(int projectId);

    /// <summary>
    /// Updates the required secrets list for a project by detecting them from the build script.
    /// </summary>
    /// <param name="projectId">Project ID.</param>
    /// <returns>The list of detected required secrets.</returns>
    Task<IReadOnlyList<string>> DetectAndUpdateRequiredSecretsAsync(int projectId);

    /// <summary>
    /// Updates the available profiles list for a project by detecting them from the build script.
    /// </summary>
    /// <param name="projectId">Project ID.</param>
    /// <returns>The list of detected available profiles.</returns>
    Task<IReadOnlyList<string>> DetectAndUpdateProfilesAsync(int projectId);
}
