// =============================================================================
// ProjectViewModels.cs
//
// Summary: View models for project-related views including list, details,
// settings, creation, and deployment status pages.
//
// This file contains all view models used by the ProjectsController to pass
// data to Razor views. Each view model is tailored to its specific view's
// needs, exposing only the data required for rendering.
//
// Design Decisions:
// - Separate view models per page to avoid over-fetching and keep views simple
// - DeploymentStatus enum abstracts build status into user-friendly categories
// - Form models are mutable (set accessors) while display models use init
// - Sorting enums allow type-safe URL parameters for the status page
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.ViewModels;

// =============================================================================
// Status Page View Models
// =============================================================================

/// <summary>
/// Deployment status for display on the status page.
/// Derived from build history: no builds = NotDeployed, last build failed = Failed,
/// last build succeeded = Deployed.
/// </summary>
public enum DeploymentStatus
{
    NotDeployed,
    Failed,
    Deployed
}

/// <summary>
/// Sort options for the status page.
/// </summary>
public enum StatusSortField
{
    Alphabetical,
    LastDeployment,
    CreatedDate
}

/// <summary>
/// Sort direction.
/// </summary>
public enum SortDirection
{
    Ascending,
    Descending
}

/// <summary>
/// View model for the project status page.
/// </summary>
public class ProjectStatusViewModel
{
    public IReadOnlyList<ProjectStatusItem> Projects { get; init; } = [];
    public StatusSortField SortField { get; init; } = StatusSortField.Alphabetical;
    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;
}

/// <summary>
/// Status information for a project card.
/// </summary>
public class ProjectStatusItem
{
    public int Id { get; init; }
    public string RepoFullName { get; init; } = "";
    public string RepoUrl { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? LastDeploymentAt { get; init; }
    public DeploymentStatus DeploymentStatus { get; init; }
    public int TotalBuilds { get; init; }
}

/// <summary>
/// View model for the projects list page.
/// </summary>
public class ProjectListViewModel
{
    public IReadOnlyList<ProjectListItem> Projects { get; init; } = [];
}

/// <summary>
/// Summary information for a project in the list.
/// </summary>
public class ProjectListItem
{
    public int Id { get; init; }
    public string RepoFullName { get; init; } = "";
    public string RepoUrl { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? LastBuildAt { get; init; }
    public BuildStatus? LastBuildStatus { get; init; }
    public int TotalBuilds { get; init; }

    // Configuration status
    public bool IsConfigured { get; init; } = true;
    public int MissingSecretsCount { get; init; }
}

/// <summary>
/// View model for project details page.
/// </summary>
public class ProjectDetailsViewModel
{
    public int Id { get; init; }
    public string RepoFullName { get; init; } = "";
    public string RepoUrl { get; init; } = "";
    public string DefaultBranch { get; init; } = "";
    public string BranchFilter { get; init; } = "";
    public bool EnablePrBuilds { get; init; }
    public int TimeoutMinutes { get; init; }
    public DateTime CreatedAt { get; init; }
    public DateTime? LastBuildAt { get; init; }

    public IReadOnlyList<BuildListItem> RecentBuilds { get; init; } = [];
    public int TotalBuilds { get; init; }

    // Configuration status
    public bool IsConfigured { get; init; } = true;
    public IReadOnlyList<string> MissingSecrets { get; init; } = [];
}

/// <summary>
/// Summary information for a build in a list.
/// </summary>
public class BuildListItem
{
    public int Id { get; init; }
    public string CommitSha { get; init; } = "";
    public string ShortCommitSha => CommitSha.Length >= 8 ? CommitSha[..8] : CommitSha;
    public string Branch { get; init; } = "";
    public string? CommitMessage { get; init; }
    public string? CommitAuthor { get; init; }
    public BuildStatus Status { get; init; }
    public BuildTrigger Trigger { get; init; }
    public DateTime QueuedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public TimeSpan? Duration { get; init; }
    public int? PullRequestNumber { get; init; }
}

/// <summary>
/// View model for the create project page.
/// Shows a form for manual repository entry.
/// </summary>
public class CreateProjectViewModel
{
    /// <summary>
    /// Pre-filled repository name (owner/repo) for retry after error.
    /// </summary>
    public string? RepoFullName { get; init; }

    /// <summary>
    /// Error message if repo lookup failed.
    /// </summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// View model for project settings page.
/// </summary>
public class ProjectSettingsViewModel
{
    public int Id { get; init; }
    public string RepoFullName { get; init; } = "";

    // Build settings
    public string BranchFilter { get; init; } = "";
    public bool EnablePrBuilds { get; init; }
    public int TimeoutMinutes { get; init; }
    public string? DockerImage { get; init; }

    // Profile settings
    public string? Profile { get; init; }
    public IReadOnlyList<string> AvailableProfiles { get; init; } = [];
    public bool IsProfileValid { get; init; } = true;

    // Required secrets (comma-separated list of required env var names)
    public string? RequiredSecrets { get; init; }

    // Notification settings
    public bool NotifyOnFailure { get; init; }
    public string? NotificationEmail { get; init; }

    // Secrets (names only, never values)
    public IReadOnlyList<string> SecretNames { get; init; } = [];

    // Configuration status
    public IReadOnlyList<string> MissingSecrets { get; init; } = [];
    public bool IsConfigured => MissingSecrets.Count == 0 && IsProfileValid;
}

/// <summary>
/// Form model for updating project settings.
/// Note: RequiredSecrets and AvailableProfiles are auto-detected from build.csando
/// and not user-editable.
/// </summary>
public class ProjectSettingsFormModel
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
/// Form model for adding a secret.
/// </summary>
public class AddSecretFormModel
{
    public string Name { get; set; } = "";
    public string Value { get; set; } = "";
}

/// <summary>
/// Form model for bulk importing secrets from .env format.
/// </summary>
public class BulkSecretsFormModel
{
    /// <summary>
    /// Raw .env file content to parse and import.
    /// Expected format: KEY=value (one per line).
    /// Lines starting with # are ignored as comments.
    /// </summary>
    public string Content { get; set; } = "";
}
