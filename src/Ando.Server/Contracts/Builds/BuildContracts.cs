// =============================================================================
// BuildContracts.cs
//
// Summary: Request and response DTOs for build-related API endpoints.
//
// These contracts define the data exchanged for viewing build details,
// retrieving logs, downloading artifacts, and managing builds (cancel/retry).
//
// Design Decisions:
// - Log entries include sequence numbers for SignalR catch-up support
// - Artifacts include metadata but not actual file content (separate download endpoint)
// - Status flags indicate available actions (CanCancel, CanRetry, IsLive)
// =============================================================================

namespace Ando.Server.Contracts.Builds;

// =============================================================================
// Build DTOs
// =============================================================================

/// <summary>
/// Full build details for detail view.
/// </summary>
/// <param name="Id">Build's unique identifier.</param>
/// <param name="ProjectId">ID of the project this build belongs to.</param>
/// <param name="ProjectName">Name of the project.</param>
/// <param name="ProjectUrl">URL to the project details page.</param>
/// <param name="CommitSha">Full Git commit SHA.</param>
/// <param name="ShortCommitSha">Shortened commit SHA for display.</param>
/// <param name="Branch">Git branch that triggered the build.</param>
/// <param name="CommitMessage">Git commit message.</param>
/// <param name="CommitAuthor">Author of the Git commit.</param>
/// <param name="PullRequestNumber">PR number if this was a PR build.</param>
/// <param name="Status">Current build status.</param>
/// <param name="Trigger">What triggered this build.</param>
/// <param name="QueuedAt">When the build was queued.</param>
/// <param name="StartedAt">When the build started executing.</param>
/// <param name="FinishedAt">When the build finished.</param>
/// <param name="Duration">Total build duration.</param>
/// <param name="StepsTotal">Total number of build steps.</param>
/// <param name="StepsCompleted">Number of completed steps.</param>
/// <param name="StepsFailed">Number of failed steps.</param>
/// <param name="ErrorMessage">Error message if the build failed.</param>
/// <param name="CanCancel">Whether the build can be cancelled.</param>
/// <param name="CanRetry">Whether the build can be retried.</param>
/// <param name="IsLive">Whether the build is currently running.</param>
/// <param name="LogEntries">Build log entries.</param>
/// <param name="Artifacts">Build artifacts.</param>
public record BuildDetailsDto(
    int Id,
    int ProjectId,
    string ProjectName,
    string ProjectUrl,
    string CommitSha,
    string ShortCommitSha,
    string Branch,
    string? CommitMessage,
    string? CommitAuthor,
    int? PullRequestNumber,
    string? Profile,
    string Status,
    string Trigger,
    DateTime QueuedAt,
    DateTime? StartedAt,
    DateTime? FinishedAt,
    TimeSpan? Duration,
    int StepsTotal,
    int StepsCompleted,
    int StepsFailed,
    string? ErrorMessage,
    bool CanCancel,
    bool CanRetry,
    bool IsLive,
    IReadOnlyList<LogEntryDto> LogEntries,
    IReadOnlyList<ArtifactDto> Artifacts
);

/// <summary>
/// Build log entry.
/// </summary>
/// <param name="Id">Log entry's unique identifier.</param>
/// <param name="Sequence">Sequence number for ordering.</param>
/// <param name="Type">Log type (stdout, stderr, system).</param>
/// <param name="Message">Log message content.</param>
/// <param name="StepName">Name of the step that generated this log.</param>
/// <param name="Timestamp">When the log entry was created.</param>
public record LogEntryDto(
    long Id,
    int Sequence,
    string Type,
    string Message,
    string? StepName,
    DateTime Timestamp
);

/// <summary>
/// Build artifact metadata.
/// </summary>
/// <param name="Id">Artifact's unique identifier.</param>
/// <param name="Name">Artifact filename.</param>
/// <param name="FormattedSize">Human-readable file size.</param>
/// <param name="SizeBytes">File size in bytes.</param>
/// <param name="CreatedAt">When the artifact was created.</param>
public record ArtifactDto(
    int Id,
    string Name,
    string FormattedSize,
    long SizeBytes,
    DateTime CreatedAt
);

// =============================================================================
// Get Build
// =============================================================================

/// <summary>
/// Response containing build details.
/// </summary>
/// <param name="Build">Full build details.</param>
public record GetBuildResponse(
    BuildDetailsDto Build
);

// =============================================================================
// Get Build Logs (for SignalR catch-up)
// =============================================================================

/// <summary>
/// Response containing log entries after a specific sequence.
/// </summary>
/// <param name="Logs">Log entries since the requested sequence.</param>
/// <param name="Status">Current build status.</param>
/// <param name="IsComplete">Whether the build has finished.</param>
public record GetBuildLogsResponse(
    IReadOnlyList<LogEntryDto> Logs,
    string Status,
    bool IsComplete
);

// =============================================================================
// Cancel Build
// =============================================================================

/// <summary>
/// Response from build cancellation.
/// </summary>
/// <param name="Success">Whether cancellation succeeded.</param>
/// <param name="Error">Error message if cancellation failed.</param>
public record CancelBuildResponse(
    bool Success,
    string? Error = null
);

// =============================================================================
// Retry Build
// =============================================================================

/// <summary>
/// Response from build retry.
/// </summary>
/// <param name="Success">Whether retry succeeded.</param>
/// <param name="NewBuildId">ID of the newly created build.</param>
/// <param name="Error">Error message if retry failed.</param>
public record RetryBuildResponse(
    bool Success,
    int? NewBuildId = null,
    string? Error = null
);
