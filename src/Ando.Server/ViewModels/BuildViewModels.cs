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
    /// <summary>
    /// Unique identifier for the build.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// ID of the project this build belongs to.
    /// </summary>
    public int ProjectId { get; init; }

    /// <summary>
    /// Name of the project (repository name).
    /// </summary>
    public string ProjectName { get; init; } = "";

    /// <summary>
    /// URL to the project details page.
    /// </summary>
    public string ProjectUrl { get; init; } = "";

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
    /// Pull request number if this was a PR build.
    /// </summary>
    public int? PullRequestNumber { get; init; }

    /// <summary>
    /// Build profile used for this build (null means default build).
    /// </summary>
    public string? Profile { get; init; }

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
    /// Total number of steps in the build.
    /// </summary>
    public int StepsTotal { get; init; }

    /// <summary>
    /// Number of steps that completed successfully.
    /// </summary>
    public int StepsCompleted { get; init; }

    /// <summary>
    /// Number of steps that failed.
    /// </summary>
    public int StepsFailed { get; init; }

    /// <summary>
    /// Error message if the build failed.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Build log entries for initial page render. Live updates via SignalR.
    /// </summary>
    public IReadOnlyList<LogEntryViewModel> LogEntries { get; init; } = [];

    /// <summary>
    /// Build artifacts produced by this build.
    /// </summary>
    public IReadOnlyList<ArtifactViewModel> Artifacts { get; init; } = [];

    /// <summary>
    /// Whether the build can be cancelled (still running).
    /// </summary>
    public bool CanCancel { get; init; }

    /// <summary>
    /// Whether the build can be retried (completed or failed).
    /// </summary>
    public bool CanRetry { get; init; }

    /// <summary>
    /// Whether this is a live build (streaming logs via SignalR).
    /// </summary>
    public bool IsLive { get; init; }
}

/// <summary>
/// View model for a log entry.
/// </summary>
public class LogEntryViewModel
{
    /// <summary>
    /// Unique identifier for the log entry.
    /// </summary>
    public long Id { get; init; }

    /// <summary>
    /// Sequence number for ordering log entries.
    /// </summary>
    public int Sequence { get; init; }

    /// <summary>
    /// Type of log entry (e.g., stdout, stderr, system).
    /// </summary>
    public string Type { get; init; } = "";

    /// <summary>
    /// Log message content.
    /// </summary>
    public string Message { get; init; } = "";

    /// <summary>
    /// Name of the build step that generated this log entry.
    /// </summary>
    public string? StepName { get; init; }

    /// <summary>
    /// When this log entry was created.
    /// </summary>
    public DateTime Timestamp { get; init; }
}

/// <summary>
/// View model for a build artifact.
/// </summary>
public class ArtifactViewModel
{
    /// <summary>
    /// Unique identifier for the artifact.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Name of the artifact file.
    /// </summary>
    public string Name { get; init; } = "";

    /// <summary>
    /// Human-readable file size (e.g., "2.5 MB").
    /// </summary>
    public string FormattedSize { get; init; } = "";

    /// <summary>
    /// When the artifact was created.
    /// </summary>
    public DateTime CreatedAt { get; init; }
}
