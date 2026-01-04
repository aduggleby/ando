// =============================================================================
// BuildViewModels.cs
//
// Summary: View models for build-related views.
// =============================================================================

using Ando.Server.Models;

namespace Ando.Server.ViewModels;

/// <summary>
/// View model for build details page.
/// </summary>
public class BuildDetailsViewModel
{
    public int Id { get; init; }
    public int ProjectId { get; init; }
    public string ProjectName { get; init; } = "";
    public string ProjectUrl { get; init; } = "";

    // Git info
    public string CommitSha { get; init; } = "";
    public string ShortCommitSha => CommitSha.Length >= 8 ? CommitSha[..8] : CommitSha;
    public string Branch { get; init; } = "";
    public string? CommitMessage { get; init; }
    public string? CommitAuthor { get; init; }
    public int? PullRequestNumber { get; init; }

    // Build state
    public BuildStatus Status { get; init; }
    public BuildTrigger Trigger { get; init; }
    public DateTime QueuedAt { get; init; }
    public DateTime? StartedAt { get; init; }
    public DateTime? FinishedAt { get; init; }
    public TimeSpan? Duration { get; init; }

    // Results
    public int StepsTotal { get; init; }
    public int StepsCompleted { get; init; }
    public int StepsFailed { get; init; }
    public string? ErrorMessage { get; init; }

    // Logs (for initial render, live updates via SignalR)
    public IReadOnlyList<LogEntryViewModel> LogEntries { get; init; } = [];

    // Artifacts
    public IReadOnlyList<ArtifactViewModel> Artifacts { get; init; } = [];

    // Actions
    public bool CanCancel { get; init; }
    public bool CanRetry { get; init; }
    public bool IsLive { get; init; }
}

/// <summary>
/// View model for a log entry.
/// </summary>
public class LogEntryViewModel
{
    public long Id { get; init; }
    public int Sequence { get; init; }
    public string Type { get; init; } = "";
    public string Message { get; init; } = "";
    public string? StepName { get; init; }
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// View model for a build artifact.
/// </summary>
public class ArtifactViewModel
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
    public string FormattedSize { get; init; } = "";
    public DateTime CreatedAt { get; init; }
}
