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
public record ProjectDetailsDto(
    int Id,
    string RepoFullName,
    string RepoUrl,
    string DefaultBranch,
    string BranchFilter,
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
public record GetProjectsResponse(
    IReadOnlyList<ProjectListItemDto> Projects
);

/// <summary>
/// Response containing project status list with sorting info.
/// </summary>
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
public record GetProjectResponse(
    ProjectDetailsDto Project
);

/// <summary>
/// Response containing project settings.
/// </summary>
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
    [Required(ErrorMessage = "Repository name is required")]
    public string RepoFullName { get; set; } = "";
}

/// <summary>
/// Response from project creation.
/// </summary>
public record CreateProjectResponse(
    bool Success,
    int? ProjectId = null,
    string? Error = null,
    string? RedirectUrl = null  // For GitHub App installation redirect
);

// =============================================================================
// Update Project Settings
// =============================================================================

/// <summary>
/// Request to update project settings.
/// </summary>
public class UpdateProjectSettingsRequest
{
    public string BranchFilter { get; set; } = "";
    public bool EnablePrBuilds { get; set; }
    public int TimeoutMinutes { get; set; } = 15;
    public string? DockerImage { get; set; }
    public string? Profile { get; set; }
    public bool NotifyOnFailure { get; set; } = true;
    public string? NotificationEmail { get; set; }
}

/// <summary>
/// Response from settings update.
/// </summary>
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
    public string? Branch { get; set; }
}

/// <summary>
/// Response from build trigger.
/// </summary>
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
    [Required(ErrorMessage = "Secret name is required")]
    [RegularExpression(@"^[A-Z_][A-Z0-9_]*$",
        ErrorMessage = "Secret name must be uppercase with underscores only (e.g., MY_SECRET)")]
    public string Name { get; set; } = "";

    [Required(ErrorMessage = "Secret value is required")]
    public string Value { get; set; } = "";
}

/// <summary>
/// Response from secret operation.
/// </summary>
public record SecretResponse(
    bool Success,
    string? Error = null
);

/// <summary>
/// Request to bulk import secrets from .env format.
/// </summary>
public class BulkImportSecretsRequest
{
    [Required(ErrorMessage = "Content is required")]
    public string Content { get; set; } = "";
}

/// <summary>
/// Response from bulk import operation.
/// </summary>
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
