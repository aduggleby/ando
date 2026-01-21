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
public record DashboardDto(
    IReadOnlyList<RecentBuildItemDto> RecentBuilds,
    int TotalProjects,
    int BuildsToday,
    int FailedToday
);

/// <summary>
/// Recent build item for dashboard.
/// </summary>
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
public record GetDashboardResponse(
    DashboardDto Dashboard
);

// =============================================================================
// Health Checks
// =============================================================================

/// <summary>
/// Basic health check response.
/// </summary>
public record HealthResponse(
    string Status,
    DateTime Timestamp
);

/// <summary>
/// Docker health check response.
/// </summary>
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
public record GitHubRepoInfo(
    long Id,
    string FullName,
    string DefaultBranch,
    bool IsPrivate
);
