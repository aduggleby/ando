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
public record GetBuildResponse(
    BuildDetailsDto Build
);

// =============================================================================
// Get Build Logs (for SignalR catch-up)
// =============================================================================

/// <summary>
/// Response containing log entries after a specific sequence.
/// </summary>
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
public record RetryBuildResponse(
    bool Success,
    int? NewBuildId = null,
    string? Error = null
);
