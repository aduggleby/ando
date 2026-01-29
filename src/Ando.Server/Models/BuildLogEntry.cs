// =============================================================================
// BuildLogEntry.cs
//
// Summary: A single log entry from a build execution.
//
// Log entries are written in real-time during build execution and streamed
// to the UI via SignalR. They capture both structured events (step start/end)
// and raw command output.
//
// Design Decisions:
// - Id is long to support high-volume logging without overflow
// - Sequence provides ordering within a build
// - Entries are immutable after creation
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// Type of log entry.
/// </summary>
public enum LogEntryType
{
    /// <summary>A build step started.</summary>
    StepStarted,

    /// <summary>A build step completed successfully.</summary>
    StepCompleted,

    /// <summary>A build step failed.</summary>
    StepFailed,

    /// <summary>Informational message.</summary>
    Info,

    /// <summary>Warning message.</summary>
    Warning,

    /// <summary>Error message.</summary>
    Error,

    /// <summary>Debug-level message.</summary>
    Debug,

    /// <summary>Raw command output.</summary>
    Output
}

/// <summary>
/// A single log entry from a build execution.
/// </summary>
public class BuildLogEntry
{
    /// <summary>
    /// Unique ID. Using long to support high-volume logging.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// ID of the build this entry belongs to.
    /// </summary>
    public int BuildId { get; set; }

    /// <summary>
    /// The build this entry belongs to.
    /// </summary>
    public Build Build { get; set; } = null!;

    /// <summary>
    /// Sequence number for ordering within a build.
    /// </summary>
    public int Sequence { get; set; }

    /// <summary>
    /// Type of log entry.
    /// </summary>
    public LogEntryType Type { get; set; }

    /// <summary>
    /// The log message content.
    /// </summary>
    public string Message { get; set; } = "";

    /// <summary>
    /// Build step name if this entry relates to a step.
    /// </summary>
    public string? StepName { get; set; }

    /// <summary>
    /// When this entry was created.
    /// </summary>
    public DateTime Timestamp { get; set; }
}
