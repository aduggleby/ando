// =============================================================================
// IBuildLogger.cs
//
// Summary: Defines the logging interface for ANDO build output.
//
// IBuildLogger provides a structured logging interface designed for build
// systems. It distinguishes between workflow-level events (start/complete),
// step-level events (started/completed/failed/skipped), and general messages
// (info/warning/error/debug).
//
// Design Decisions:
// - Structured events (StepStarted, WorkflowCompleted) instead of free-form logs
//   allows implementations to format output appropriately (progress bars, etc.)
// - Verbosity levels allow users to control output detail
// - Interface enables multiple implementations (console, file, test doubles)
// - Duration parameters on completion events support timing display
// =============================================================================

namespace Ando.Logging;

/// <summary>
/// Log verbosity levels from least to most output.
/// </summary>
public enum LogLevel
{
    /// <summary>Only errors are shown.</summary>
    Quiet,
    /// <summary>Errors and warnings are shown.</summary>
    Minimal,
    /// <summary>Standard output including step progress.</summary>
    Normal,
    /// <summary>Verbose output including debug information.</summary>
    Detailed
}

/// <summary>
/// Interface for structured build logging.
/// Provides methods for workflow events, step events, and general messages.
/// </summary>
public interface IBuildLogger
{
    /// <summary>
    /// Current verbosity level. Messages below this level are suppressed.
    /// </summary>
    LogLevel Verbosity { get; set; }

    // Step lifecycle events - called by WorkflowRunner as steps execute.

    /// <summary>Logs that a step has started executing.</summary>
    void StepStarted(string stepName, string? context = null);

    /// <summary>Logs that a step completed successfully.</summary>
    void StepCompleted(string stepName, TimeSpan duration, string? context = null);

    /// <summary>Logs that a step failed.</summary>
    void StepFailed(string stepName, TimeSpan duration, string? message = null);

    /// <summary>Logs that a step was skipped (e.g., condition not met).</summary>
    void StepSkipped(string stepName, string? reason = null);

    // General message logging at different severity levels.

    /// <summary>Logs an informational message (Normal+ verbosity).</summary>
    void Info(string message);

    /// <summary>Logs a warning message (Minimal+ verbosity).</summary>
    void Warning(string message);

    /// <summary>Logs an error message (always shown).</summary>
    void Error(string message);

    /// <summary>Logs a debug message (Detailed verbosity only).</summary>
    void Debug(string message);

    // Workflow lifecycle events - called at build start/end.

    /// <summary>Logs that a workflow has started.</summary>
    void WorkflowStarted(string workflowName, string? scriptPath = null, int totalSteps = 0);

    /// <summary>Logs that a workflow has completed.</summary>
    void WorkflowCompleted(string workflowName, TimeSpan duration, int stepsRun, int stepsFailed);
}
