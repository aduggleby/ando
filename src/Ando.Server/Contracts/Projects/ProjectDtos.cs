// =============================================================================
// ProjectDtos.cs
//
// Summary: Core DTOs for project and build summary/detail payloads.
//
// These DTOs are shared across multiple project endpoints and represent the
// canonical shapes returned to the SPA.
// =============================================================================

namespace Ando.Server.Contracts.Projects;

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
    string? LastBuildGitVersionTag,
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
    string? GitVersionTag,
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
