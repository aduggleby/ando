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
    /// <summary>
    /// List of projects with their deployment status.
    /// </summary>
    public IReadOnlyList<ProjectStatusItem> Projects { get; init; } = [];

    /// <summary>
    /// Field used for sorting the project list.
    /// </summary>
    public StatusSortField SortField { get; init; } = StatusSortField.Alphabetical;

    /// <summary>
    /// Direction of sorting (ascending or descending).
    /// </summary>
    public SortDirection SortDirection { get; init; } = SortDirection.Ascending;
}

/// <summary>
/// Status information for a project card.
/// </summary>
public class ProjectStatusItem
{
    /// <summary>
    /// Unique identifier for the project.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Full repository name in owner/repo format.
    /// </summary>
    public string RepoFullName { get; init; } = "";

    /// <summary>
    /// URL to the repository on GitHub.
    /// </summary>
    public string RepoUrl { get; init; } = "";

    /// <summary>
    /// When the project was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the last successful deployment occurred.
    /// </summary>
    public DateTime? LastDeploymentAt { get; init; }

    /// <summary>
    /// Current deployment status (NotDeployed, Failed, or Deployed).
    /// </summary>
    public DeploymentStatus DeploymentStatus { get; init; }

    /// <summary>
    /// Total number of builds for this project.
    /// </summary>
    public int TotalBuilds { get; init; }
}

/// <summary>
/// View model for the projects list page.
/// </summary>
public class ProjectListViewModel
{
    /// <summary>
    /// List of projects owned by the current user.
    /// </summary>
    public IReadOnlyList<ProjectListItem> Projects { get; init; } = [];

    /// <summary>
    /// Field used for sorting the project list.
    /// </summary>
    public StatusSortField SortField { get; init; } = StatusSortField.LastDeployment;

    /// <summary>
    /// Direction of sorting (ascending or descending).
    /// </summary>
    public SortDirection SortDirection { get; init; } = SortDirection.Descending;
}

/// <summary>
/// Summary information for a project in the list.
/// </summary>
public class ProjectListItem
{
    /// <summary>
    /// Unique identifier for the project.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Full repository name in owner/repo format.
    /// </summary>
    public string RepoFullName { get; init; } = "";

    /// <summary>
    /// URL to the repository on GitHub.
    /// </summary>
    public string RepoUrl { get; init; } = "";

    /// <summary>
    /// When the project was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the last build was started, if any.
    /// </summary>
    public DateTime? LastBuildAt { get; init; }

    /// <summary>
    /// Status of the most recent build.
    /// </summary>
    public BuildStatus? LastBuildStatus { get; init; }

    /// <summary>
    /// Total number of builds for this project.
    /// </summary>
    public int TotalBuilds { get; init; }

    /// <summary>
    /// Whether the project has all required secrets configured.
    /// </summary>
    public bool IsConfigured { get; init; } = true;

    /// <summary>
    /// Number of required secrets that are not yet configured.
    /// </summary>
    public int MissingSecretsCount { get; init; }

    /// <summary>
    /// Current deployment status inferred from build history.
    /// </summary>
    public DeploymentStatus DeploymentStatus { get; init; } = DeploymentStatus.NotDeployed;

    /// <summary>
    /// Last deployment/build completion time used for status display.
    /// </summary>
    public DateTime? LastDeploymentAt { get; init; }
}

/// <summary>
/// View model for project details page.
/// </summary>
public class ProjectDetailsViewModel
{
    /// <summary>
    /// Unique identifier for the project.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Full repository name in owner/repo format.
    /// </summary>
    public string RepoFullName { get; init; } = "";

    /// <summary>
    /// URL to the repository on GitHub.
    /// </summary>
    public string RepoUrl { get; init; } = "";

    /// <summary>
    /// Default branch of the repository.
    /// </summary>
    public string DefaultBranch { get; init; } = "";

    /// <summary>
    /// Branch filter pattern for triggering builds.
    /// </summary>
    public string BranchFilter { get; init; } = "";

    /// <summary>
    /// Selected build profile (null means default build).
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>
    /// Whether pull request builds are enabled.
    /// </summary>
    public bool EnablePrBuilds { get; init; }

    /// <summary>
    /// Maximum build time in minutes before timeout.
    /// </summary>
    public int TimeoutMinutes { get; init; }

    /// <summary>
    /// When the project was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// When the last build was started, if any.
    /// </summary>
    public DateTime? LastBuildAt { get; init; }

    /// <summary>
    /// List of recent builds for this project.
    /// </summary>
    public IReadOnlyList<BuildListItem> RecentBuilds { get; init; } = [];

    /// <summary>
    /// Total number of builds for this project.
    /// </summary>
    public int TotalBuilds { get; init; }

    /// <summary>
    /// Whether the project has all required secrets configured.
    /// </summary>
    public bool IsConfigured { get; init; } = true;

    /// <summary>
    /// Names of required secrets that are not yet configured.
    /// </summary>
    public IReadOnlyList<string> MissingSecrets { get; init; } = [];
}

/// <summary>
/// Summary information for a build in a list.
/// </summary>
public class BuildListItem
{
    /// <summary>
    /// Unique identifier for the build.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Full Git commit SHA.
    /// </summary>
    public string CommitSha { get; init; } = "";

    /// <summary>
    /// Shortened commit SHA for display (first 8 characters).
    /// </summary>
    public string ShortCommitSha => CommitSha.Length >= 8 ? CommitSha[..8] : CommitSha;

    /// <summary>
    /// Git branch that triggered the build.
    /// </summary>
    public string Branch { get; init; } = "";

    /// <summary>
    /// Git commit message.
    /// </summary>
    public string? CommitMessage { get; init; }

    /// <summary>
    /// Author of the Git commit.
    /// </summary>
    public string? CommitAuthor { get; init; }

    /// <summary>
    /// Current status of the build.
    /// </summary>
    public BuildStatus Status { get; init; }

    /// <summary>
    /// What triggered this build (push, PR, manual, etc.).
    /// </summary>
    public BuildTrigger Trigger { get; init; }

    /// <summary>
    /// When the build was added to the queue.
    /// </summary>
    public DateTime QueuedAt { get; init; }

    /// <summary>
    /// When the build started executing.
    /// </summary>
    public DateTime? StartedAt { get; init; }

    /// <summary>
    /// When the build finished executing.
    /// </summary>
    public DateTime? FinishedAt { get; init; }

    /// <summary>
    /// Total build duration from start to finish.
    /// </summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>
    /// Pull request number if this was a PR build.
    /// </summary>
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
    /// <summary>
    /// Unique identifier for the project.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Full repository name in owner/repo format.
    /// </summary>
    public string RepoFullName { get; init; } = "";

    /// <summary>
    /// Branch filter pattern for triggering builds.
    /// </summary>
    public string BranchFilter { get; init; } = "";

    /// <summary>
    /// Whether pull request builds are enabled.
    /// </summary>
    public bool EnablePrBuilds { get; init; }

    /// <summary>
    /// Maximum build time in minutes before timeout.
    /// </summary>
    public int TimeoutMinutes { get; init; }

    /// <summary>
    /// Custom Docker image to use for builds.
    /// </summary>
    public string? DockerImage { get; init; }

    /// <summary>
    /// Selected build profile name.
    /// </summary>
    public string? Profile { get; init; }

    /// <summary>
    /// Whether manual profile override is enabled in the settings form.
    /// </summary>
    public bool ManualProfileOverride { get; init; }

    /// <summary>
    /// Manual profile value used when override is enabled.
    /// </summary>
    public string? ManualProfile { get; init; }

    /// <summary>
    /// List of available build profiles defined in the repository.
    /// </summary>
    public IReadOnlyList<string> AvailableProfiles { get; init; } = [];

    /// <summary>
    /// Whether the selected profile exists in the available profiles.
    /// </summary>
    public bool IsProfileValid { get; init; } = true;

    /// <summary>
    /// Comma-separated list of required environment variable names.
    /// </summary>
    public string? RequiredSecrets { get; init; }

    /// <summary>
    /// Whether to send email notifications on build failure.
    /// </summary>
    public bool NotifyOnFailure { get; init; }

    /// <summary>
    /// Email address to send failure notifications to.
    /// </summary>
    public string? NotificationEmail { get; init; }

    /// <summary>
    /// Names of secrets configured for this project.
    /// </summary>
    public IReadOnlyList<string> SecretNames { get; init; } = [];

    /// <summary>
    /// Names of required secrets that are not yet configured.
    /// </summary>
    public IReadOnlyList<string> MissingSecrets { get; init; } = [];

    /// <summary>
    /// Whether the project has all required secrets and a valid profile.
    /// </summary>
    public bool IsConfigured => MissingSecrets.Count == 0 && IsProfileValid;
}

/// <summary>
/// Form model for updating project settings.
/// Note: RequiredSecrets and AvailableProfiles are auto-detected from build.csando
/// and not user-editable.
/// </summary>
public class ProjectSettingsFormModel
{
    /// <summary>
    /// Branch filter pattern for triggering builds.
    /// </summary>
    public string BranchFilter { get; set; } = "";

    /// <summary>
    /// Whether pull request builds are enabled.
    /// </summary>
    public bool EnablePrBuilds { get; set; }

    /// <summary>
    /// Maximum build time in minutes before timeout.
    /// </summary>
    public int TimeoutMinutes { get; set; } = 15;

    /// <summary>
    /// Custom Docker image to use for builds.
    /// </summary>
    public string? DockerImage { get; set; }

    /// <summary>
    /// Selected build profile name.
    /// </summary>
    public string? Profile { get; set; }

    /// <summary>
    /// Whether to manually override profile validation and set a profile value directly.
    /// </summary>
    public bool ManualProfileOverride { get; set; }

    /// <summary>
    /// Manual profile value used when override is enabled.
    /// </summary>
    public string? ManualProfile { get; set; }

    /// <summary>
    /// Whether to send email notifications on build failure.
    /// </summary>
    public bool NotifyOnFailure { get; set; } = true;

    /// <summary>
    /// Email address to send failure notifications to.
    /// </summary>
    public string? NotificationEmail { get; set; }
}

/// <summary>
/// Form model for adding a secret.
/// </summary>
public class AddSecretFormModel
{
    /// <summary>
    /// Name of the secret (environment variable name).
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Secret value to store.
    /// </summary>
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
