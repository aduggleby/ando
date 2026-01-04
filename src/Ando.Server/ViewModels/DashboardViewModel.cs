// =============================================================================
// DashboardViewModel.cs
//
// Summary: View model for the main dashboard page.
//
// Contains data for displaying recent builds, project counts, and build
// statistics across all projects.
// =============================================================================

namespace Ando.Server.ViewModels;

/// <summary>
/// View model for the main dashboard page.
/// </summary>
public class DashboardViewModel
{
    /// <summary>
    /// Recent builds across all projects.
    /// </summary>
    public IReadOnlyList<RecentBuildItem> RecentBuilds { get; init; } = [];

    /// <summary>
    /// Total number of projects owned by the user.
    /// </summary>
    public int TotalProjects { get; init; }

    /// <summary>
    /// Number of builds started today.
    /// </summary>
    public int BuildsToday { get; init; }

    /// <summary>
    /// Number of failed builds today.
    /// </summary>
    public int FailedToday { get; init; }
}

/// <summary>
/// Summary information for a recent build.
/// </summary>
public record RecentBuildItem(
    int BuildId,
    string ProjectName,
    string Branch,
    string CommitSha,
    string Status,
    DateTime? StartedAt,
    TimeSpan? Duration
);
