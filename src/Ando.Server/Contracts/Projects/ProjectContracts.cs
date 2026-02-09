// =============================================================================
// ProjectContracts.cs
//
// Summary: Request and response DTOs for project management API endpoints.
//
// These contracts define the data exchanged for project CRUD operations,
// settings management, secret handling, and build triggering.
//
// Design Decisions:
// - Separate DTOs for list vs detail views to optimize payload size
// - Secret values are never returned in responses (security)
// - Pagination support for project lists
// - Enum serialization as strings for frontend readability
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Ando.Server.Models;

namespace Ando.Server.Contracts.Projects;

// =============================================================================
// Project DTOs
// =============================================================================

/// <summary>
/// Project summary for list views.
/// </summary>
/// <param name="Id">Project's unique identifier.</param>
/// <param name="RepoFullName">Full repository name (owner/repo).</param>
/// <param name="RepoUrl">URL to the repository on GitHub.</param>
/// <param name="CreatedAt">When the project was created.</param>
/// <param name="LastBuildAt">When the last build was started.</param>
/// <param name="LastBuildStatus">Status of the most recent build.</param>
/// <param name="TotalBuilds">Total number of builds.</param>
/// <param name="IsConfigured">Whether all required secrets are configured.</param>
/// <param name="MissingSecretsCount">Number of missing required secrets.</param>
public record ProjectListItemDto(
    int Id,
    string RepoFullName,
    string RepoUrl,
    DateTime CreatedAt,
    DateTime? LastBuildAt,
    string? LastBuildStatus,
    int TotalBuilds,
    bool IsConfigured,
    int MissingSecretsCount
);

/// <summary>
/// Full project details for detail view.
/// </summary>
/// <param name="Id">Project's unique identifier.</param>
/// <param name="RepoFullName">Full repository name (owner/repo).</param>
/// <param name="RepoUrl">URL to the repository on GitHub.</param>
/// <param name="DefaultBranch">Repository's default branch.</param>
/// <param name="BranchFilter">Branch filter pattern for builds.</param>
/// <param name="EnablePrBuilds">Whether PR builds are enabled.</param>
/// <param name="TimeoutMinutes">Build timeout in minutes.</param>
/// <param name="CreatedAt">When the project was created.</param>
/// <param name="LastBuildAt">When the last build was started.</param>
/// <param name="TotalBuilds">Total number of builds.</param>
/// <param name="IsConfigured">Whether all required secrets are configured.</param>
/// <param name="MissingSecrets">Names of missing required secrets.</param>
/// <param name="RecentBuilds">List of recent builds.</param>
public record ProjectDetailsDto(
    int Id,
    string RepoFullName,
    string RepoUrl,
    string DefaultBranch,
    string BranchFilter,
    string? Profile,
    bool EnablePrBuilds,
    int TimeoutMinutes,
    DateTime CreatedAt,
    DateTime? LastBuildAt,
    int TotalBuilds,
    bool IsConfigured,
    IReadOnlyList<string> MissingSecrets,
    IReadOnlyList<BuildListItemDto> RecentBuilds
);

/// <summary>
/// Project settings for the settings page.
/// </summary>
/// <param name="Id">Project's unique identifier.</param>
/// <param name="RepoFullName">Full repository name (owner/repo).</param>
/// <param name="BranchFilter">Branch filter pattern for builds.</param>
/// <param name="EnablePrBuilds">Whether PR builds are enabled.</param>
/// <param name="TimeoutMinutes">Build timeout in minutes.</param>
/// <param name="DockerImage">Custom Docker image for builds.</param>
/// <param name="Profile">Selected build profile name.</param>
/// <param name="AvailableProfiles">Available build profiles from repository.</param>
/// <param name="IsProfileValid">Whether selected profile exists.</param>
/// <param name="RequiredSecrets">Comma-separated list of required secret names.</param>
/// <param name="NotifyOnFailure">Whether to send failure notifications.</param>
/// <param name="NotificationEmail">Email for failure notifications.</param>
/// <param name="SecretNames">Names of configured secrets.</param>
/// <param name="MissingSecrets">Names of missing required secrets.</param>
public record ProjectSettingsDto(
    int Id,
    string RepoFullName,
    string BranchFilter,
    bool EnablePrBuilds,
    int TimeoutMinutes,
    string? DockerImage,
    string? Profile,
    IReadOnlyList<string> AvailableProfiles,
    bool IsProfileValid,
    string? RequiredSecrets,
    bool NotifyOnFailure,
    string? NotificationEmail,
    IReadOnlyList<string> SecretNames,
    IReadOnlyList<string> MissingSecrets
);

/// <summary>
/// Project status for deployment status dashboard.
/// </summary>
/// <param name="Id">Project's unique identifier.</param>
/// <param name="RepoFullName">Full repository name (owner/repo).</param>
/// <param name="RepoUrl">URL to the repository on GitHub.</param>
/// <param name="CreatedAt">When the project was created.</param>
/// <param name="LastDeploymentAt">When the last successful deployment occurred.</param>
/// <param name="DeploymentStatus">Current deployment status.</param>
/// <param name="TotalBuilds">Total number of builds.</param>
public record ProjectStatusDto(
    int Id,
    string RepoFullName,
    string RepoUrl,
    DateTime CreatedAt,
    DateTime? LastDeploymentAt,
    string DeploymentStatus,
    int TotalBuilds
);

// =============================================================================
// List Projects
// =============================================================================

/// <summary>
/// Response containing list of projects.
/// </summary>
/// <param name="Projects">List of projects owned by the user.</param>
public record GetProjectsResponse(
    IReadOnlyList<ProjectListItemDto> Projects
);

/// <summary>
/// Response containing project status list with sorting info.
/// </summary>
/// <param name="Projects">List of project statuses.</param>
/// <param name="SortField">Field used for sorting.</param>
/// <param name="SortDirection">Sort direction (Ascending/Descending).</param>
public record GetProjectsStatusResponse(
    IReadOnlyList<ProjectStatusDto> Projects,
    string SortField,
    string SortDirection
);

// =============================================================================
// Get Project
// =============================================================================

/// <summary>
/// Response containing project details.
/// </summary>
/// <param name="Project">Full project details.</param>
public record GetProjectResponse(
    ProjectDetailsDto Project
);

/// <summary>
/// Response containing project settings.
/// </summary>
/// <param name="Settings">Project settings.</param>
public record GetProjectSettingsResponse(
    ProjectSettingsDto Settings
);

// =============================================================================
// Create Project
// =============================================================================

/// <summary>
/// Request to create a new project from a GitHub repository.
/// </summary>
public class CreateProjectRequest
{
    /// <summary>
    /// Full repository name in owner/repo format.
    /// </summary>
    public string RepoFullName { get; set; } = "";

    /// <summary>
    /// Back-compat with older/alternate clients that send a URL field.
    /// The server normalizes URLs into owner/repo format.
    /// </summary>
    public string? RepoUrl { get; set; }
}

/// <summary>
/// Response from project creation.
/// </summary>
/// <param name="Success">Whether creation succeeded.</param>
/// <param name="ProjectId">ID of the created project.</param>
/// <param name="Error">Error message if creation failed.</param>
/// <param name="RedirectUrl">URL for GitHub App installation if needed.</param>
public record CreateProjectResponse(
    bool Success,
    int? ProjectId = null,
    string? Error = null,
    string? RedirectUrl = null
);

// =============================================================================
// Update Project Settings
// =============================================================================

/// <summary>
/// Request to update project settings.
/// </summary>
public class UpdateProjectSettingsRequest
{
    /// <summary>
    /// Branch filter pattern for triggering builds.
    /// </summary>
    public string BranchFilter { get; set; } = "";

    /// <summary>
    /// Whether to enable pull request builds.
    /// </summary>
    public bool EnablePrBuilds { get; set; }

    /// <summary>
    /// Build timeout in minutes.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Custom Docker image for builds.
    /// </summary>
    public string? DockerImage { get; set; }

    /// <summary>
    /// Build profile name to use.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Whether to send failure notifications.
    /// </summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>
    /// Email address for failure notifications.
    /// </summary>
    public string? NotificationEmail { get; set; }
}

/// <summary>
/// Response from settings update.
/// </summary>
/// <param name="Success">Whether update succeeded.</param>
/// <param name="Error">Error message if update failed.</param>
public record UpdateProjectSettingsResponse(
    bool Success,
    string? Error = null
);

// =============================================================================
// Delete Project
// =============================================================================

/// <summary>
/// Response from project deletion.
/// </summary>
/// <param name="Success">Whether deletion succeeded.</param>
/// <param name="Error">Error message if deletion failed.</param>
public record DeleteProjectResponse(
    bool Success,
    string? Error = null
);

// =============================================================================
// Trigger Build
// =============================================================================

/// <summary>
/// Request to manually trigger a build.
/// </summary>
public class TriggerBuildRequest
{
    /// <summary>
    /// Branch to build (defaults to repository's default branch).
    /// </summary>
    public string? Branch { get; set; }
}

/// <summary>
/// Response from build trigger.
/// </summary>
/// <param name="Success">Whether the build was triggered.</param>
/// <param name="BuildId">ID of the created build.</param>
/// <param name="Error">Error message if trigger failed.</param>
public record TriggerBuildResponse(
    bool Success,
    int? BuildId = null,
    string? Error = null
);

// =============================================================================
// Secrets
// =============================================================================

/// <summary>
/// Request to add or update a secret.
/// </summary>
public class SetSecretRequest
{
    /// <summary>
    /// Secret name (uppercase with underscores, e.g., MY_SECRET).
    /// </summary>
    [Required(ErrorMessage = "Secret name is required")]
    [RegularExpression(@"^[A-Z_][A-Z0-9_]*$",
        ErrorMessage = "Secret name must be uppercase with underscores only (e.g., MY_SECRET)")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Secret value to store.
    /// </summary>
    [Required(ErrorMessage = "Secret value is required")]
    public string Value { get; set; } = "";
}

/// <summary>
/// Response from secret operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Error">Error message if operation failed.</param>
public record SecretResponse(
    bool Success,
    string? Error = null
);

/// <summary>
/// Request to bulk import secrets from .env format.
/// </summary>
public class BulkImportSecretsRequest
{
    /// <summary>
    /// Content in .env format (KEY=value, one per line).
    /// </summary>
    [Required(ErrorMessage = "Content is required")]
    public string Content { get; set; } = "";
}

/// <summary>
/// Response from bulk import operation.
/// </summary>
/// <param name="Success">Whether import succeeded.</param>
/// <param name="ImportedCount">Number of secrets imported.</param>
/// <param name="Errors">List of import errors, if any.</param>
public record BulkImportSecretsResponse(
    bool Success,
    int ImportedCount,
    IReadOnlyList<string>? Errors = null
);

// =============================================================================
// Refresh Secrets
// =============================================================================

/// <summary>
/// Response from refreshing required secrets detection.
/// </summary>
/// <param name="Success">Whether refresh succeeded.</param>
/// <param name="DetectedSecrets">Required secrets detected from build.csando.</param>
/// <param name="DetectedProfiles">Available profiles detected from build.csando.</param>
public record RefreshSecretsResponse(
    bool Success,
    IReadOnlyList<string> DetectedSecrets,
    IReadOnlyList<string> DetectedProfiles
);

// =============================================================================
// Build List Item (shared with Builds contracts)
// =============================================================================

/// <summary>
/// Build summary for list views.
/// </summary>
/// <param name="Id">Build's unique identifier.</param>
/// <param name="CommitSha">Full Git commit SHA.</param>
/// <param name="ShortCommitSha">Shortened commit SHA for display.</param>
/// <param name="Branch">Git branch that triggered the build.</param>
/// <param name="CommitMessage">Git commit message.</param>
/// <param name="CommitAuthor">Author of the Git commit.</param>
/// <param name="Status">Current build status.</param>
/// <param name="Trigger">What triggered this build.</param>
/// <param name="QueuedAt">When the build was queued.</param>
/// <param name="StartedAt">When the build started executing.</param>
/// <param name="FinishedAt">When the build finished.</param>
/// <param name="Duration">Total build duration.</param>
/// <param name="PullRequestNumber">PR number if this was a PR build.</param>
public record BuildListItemDto(
    int Id,
    string CommitSha,
    string ShortCommitSha,
    string Branch,
    string? CommitMessage,
    string? CommitAuthor,
    string Status,
    string Trigger,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    TimeSpan? Duration,
    int? PullRequestNumber
);
