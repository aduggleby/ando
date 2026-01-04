// =============================================================================
// Build.cs
//
// Summary: Represents a single build execution for a project.
//
// A build is created when a webhook is received or manually triggered. It
// tracks the full lifecycle from queued through completion, including timing,
// step progress, and final status.
//
// Design Decisions:
// - Builds are immutable after completion (status, timestamps, results)
// - HangfireJobId enables build cancellation
// - Duration is stored for quick access without recalculating
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// The current status of a build.
/// </summary>
public enum BuildStatus
{
    /// <summary>Build is waiting in queue.</summary>
    Queued,

    /// <summary>Build is currently executing.</summary>
    Running,

    /// <summary>Build completed successfully.</summary>
    Success,

    /// <summary>Build failed (step failure or error).</summary>
    Failed,

    /// <summary>Build was cancelled by user.</summary>
    Cancelled,

    /// <summary>Build exceeded timeout limit.</summary>
    TimedOut
}

/// <summary>
/// What triggered the build.
/// </summary>
public enum BuildTrigger
{
    /// <summary>Push to a branch.</summary>
    Push,

    /// <summary>Pull request opened or updated.</summary>
    PullRequest,

    /// <summary>Manually triggered via UI.</summary>
    Manual
}

/// <summary>
/// A single build execution for a project.
/// </summary>
public class Build
{
    public int Id { get; set; }

    public int ProjectId { get; set; }

    /// <summary>
    /// The project this build belongs to.
    /// </summary>
    public Project Project { get; set; } = null!;

    // -------------------------------------------------------------------------
    // Git Information
    // -------------------------------------------------------------------------

    /// <summary>
    /// Full commit SHA being built.
    /// </summary>
    public string CommitSha { get; set; } = "";

    /// <summary>
    /// Branch name (e.g., "main", "feature/xyz").
    /// </summary>
    public string Branch { get; set; } = "";

    /// <summary>
    /// Commit message (first line).
    /// </summary>
    public string? CommitMessage { get; set; }

    /// <summary>
    /// Commit author name.
    /// </summary>
    public string? CommitAuthor { get; set; }

    /// <summary>
    /// Pull request number if this is a PR build.
    /// </summary>
    public int? PullRequestNumber { get; set; }

    // -------------------------------------------------------------------------
    // Build State
    // -------------------------------------------------------------------------

    /// <summary>
    /// Current build status.
    /// </summary>
    public BuildStatus Status { get; set; } = BuildStatus.Queued;

    /// <summary>
    /// What triggered this build.
    /// </summary>
    public BuildTrigger Trigger { get; set; }

    /// <summary>
    /// When the build was queued.
    /// </summary>
    public DateTime QueuedAt { get; set; }

    /// <summary>
    /// When the build started executing.
    /// </summary>
    public DateTime? StartedAt { get; set; }

    /// <summary>
    /// When the build finished (success, failure, or cancellation).
    /// </summary>
    public DateTime? FinishedAt { get; set; }

    /// <summary>
    /// Total build duration. Stored for quick access.
    /// </summary>
    public TimeSpan? Duration { get; set; }

    // -------------------------------------------------------------------------
    // Results
    // -------------------------------------------------------------------------

    /// <summary>
    /// Total number of build steps.
    /// </summary>
    public int StepsTotal { get; set; }

    /// <summary>
    /// Number of steps that completed successfully.
    /// </summary>
    public int StepsCompleted { get; set; }

    /// <summary>
    /// Number of steps that failed.
    /// </summary>
    public int StepsFailed { get; set; }

    /// <summary>
    /// Error message if the build failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    // -------------------------------------------------------------------------
    // Job Tracking
    // -------------------------------------------------------------------------

    /// <summary>
    /// Hangfire job ID for cancellation support.
    /// </summary>
    public string? HangfireJobId { get; set; }

    // -------------------------------------------------------------------------
    // Navigation Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Log entries for this build.
    /// </summary>
    public ICollection<BuildLogEntry> LogEntries { get; set; } = [];

    /// <summary>
    /// Artifacts produced by this build.
    /// </summary>
    public ICollection<BuildArtifact> Artifacts { get; set; } = [];

    // -------------------------------------------------------------------------
    // Helper Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Short commit SHA for display (first 8 characters).
    /// </summary>
    public string ShortCommitSha => CommitSha.Length >= 8 ? CommitSha[..8] : CommitSha;

    /// <summary>
    /// Whether the build is in a terminal state.
    /// </summary>
    public bool IsFinished => Status is BuildStatus.Success or BuildStatus.Failed
        or BuildStatus.Cancelled or BuildStatus.TimedOut;

    /// <summary>
    /// Whether the build can be cancelled.
    /// </summary>
    public bool CanCancel => Status is BuildStatus.Running or BuildStatus.Queued;

    /// <summary>
    /// Whether the build can be retried.
    /// </summary>
    public bool CanRetry => IsFinished;
}
