// =============================================================================
// HomeContracts.cs
//
// Summary: Request and response DTOs for dashboard and health check endpoints.
//
// These contracts define the data exchanged for the user dashboard (recent
// builds, statistics) and system health check endpoints.
//
// Design Decisions:
// - Dashboard data is user-scoped (only their projects)
// - Health checks return consistent status format
// =============================================================================

namespace Ando.Server.Contracts.Home;

// =============================================================================
// Dashboard
// =============================================================================

/// <summary>
/// User dashboard data with recent builds and statistics.
/// </summary>
/// <param name="RecentBuilds">List of recent builds across all projects.</param>
/// <param name="TotalProjects">Total number of projects owned by the user.</param>
/// <param name="BuildsToday">Number of builds started today.</param>
/// <param name="FailedToday">Number of failed builds today.</param>
public record DashboardDto(
    IReadOnlyList<RecentBuildItemDto> RecentBuilds,
    int TotalProjects,
    int BuildsToday,
    int FailedToday
);

/// <summary>
/// Recent build item for dashboard.
/// </summary>
/// <param name="Id">Build's unique identifier.</param>
/// <param name="ProjectName">Name of the project.</param>
/// <param name="Branch">Git branch that triggered the build.</param>
/// <param name="ShortCommitSha">Shortened commit SHA for display.</param>
/// <param name="Status">Current build status.</param>
/// <param name="StartedAt">When the build started.</param>
/// <param name="Duration">Total build duration.</param>
public record RecentBuildItemDto(
    int Id,
    string ProjectName,
    string Branch,
    string ShortCommitSha,
    string Status,
    DateTime? StartedAt,
    TimeSpan? Duration
);

/// <summary>
/// Response containing dashboard data.
/// </summary>
/// <param name="Dashboard">User's dashboard data.</param>
public record GetDashboardResponse(
    DashboardDto Dashboard
);

// =============================================================================
// Health Checks
// =============================================================================

/// <summary>
/// Basic health check response.
/// </summary>
/// <param name="Status">Health status (Healthy, Degraded, Unhealthy).</param>
/// <param name="Timestamp">When the check was performed.</param>
public record HealthResponse(
    string Status,
    DateTime Timestamp
);

/// <summary>
/// Docker health check response.
/// </summary>
/// <param name="Status">Docker status (Healthy, Unhealthy).</param>
/// <param name="Message">Success message if healthy.</param>
/// <param name="Error">Error message if unhealthy.</param>
/// <param name="Details">Additional details about Docker status.</param>
/// <param name="Timestamp">When the check was performed.</param>
public record DockerHealthResponse(
    string Status,
    string? Message = null,
    string? Error = null,
    string? Details = null,
    DateTime? Timestamp = null
);

/// <summary>
/// GitHub health check response.
/// </summary>
/// <param name="Status">GitHub connection status.</param>
/// <param name="Message">Success message if healthy.</param>
/// <param name="Error">Error message if unhealthy.</param>
/// <param name="Repository">Repository info if test repo is accessible.</param>
/// <param name="InstallationId">GitHub App installation ID.</param>
/// <param name="Timestamp">When the check was performed.</param>
public record GitHubHealthResponse(
    string Status,
    string? Message = null,
    string? Error = null,
    GitHubRepoInfo? Repository = null,
    long? InstallationId = null,
    DateTime? Timestamp = null
);

/// <summary>
/// GitHub repository info for health check.
/// </summary>
/// <param name="Id">GitHub repository ID.</param>
/// <param name="FullName">Full repository name (owner/repo).</param>
/// <param name="DefaultBranch">Repository's default branch.</param>
/// <param name="IsPrivate">Whether the repository is private.</param>
public record GitHubRepoInfo(
    long Id,
    string FullName,
    string DefaultBranch,
    bool IsPrivate
);
