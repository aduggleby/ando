// =============================================================================
// IBuildLogger.cs
//
// Summary: Defines the logging interfaces for ANDO build output.
//
// This file defines a family of logging interfaces following the Interface
// Segregation Principle (ISP). Clients can depend on only the logging
// capabilities they need:
//
// - IMessageLogger: Basic message logging (Info, Warning, Error, Debug)
// - IStepLogger: Build step lifecycle events (Started, Completed, Failed)
// - IWorkflowLogger: Workflow lifecycle events (Started, Completed)
// - IBuildLogger: Combined interface for full build logging functionality
//
// Design Decisions:
// - Interface segregation allows components to depend on minimal interfaces
// - IBuildLogger extends all three for backward compatibility
// - Structured events enable rich formatting (progress bars, etc.)
// - Verbosity on IMessageLogger controls message filtering
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
/// Interface for basic message logging at different severity levels.
/// Use this interface when you only need simple message output.
/// </summary>
public interface IMessageLogger
{
    /// <summary>
    /// Current verbosity level. Messages below this level are suppressed.
    /// </summary>
    LogLevel Verbosity { get; set; }

    /// <summary>Logs an informational message (Normal+ verbosity).</summary>
    void Info(string message);

    /// <summary>Logs a warning message (Minimal+ verbosity).</summary>
    void Warning(string message);

    /// <summary>Logs an error message (always shown).</summary>
    void Error(string message);

    /// <summary>Logs a debug message (Detailed verbosity only).</summary>
    void Debug(string message);
}

/// <summary>
/// Interface for build step lifecycle events.
/// Use this interface for components that track step execution.
/// </summary>
public interface IStepLogger
{
    /// <summary>Logs that a step has started executing.</summary>
    void StepStarted(string stepName, string? context = null);

    /// <summary>Logs that a step completed successfully.</summary>
    void StepCompleted(string stepName, TimeSpan duration, string? context = null);

    /// <summary>Logs that a step failed.</summary>
    void StepFailed(string stepName, TimeSpan duration, string? message = null);

    /// <summary>Logs that a step was skipped (e.g., condition not met).</summary>
    void StepSkipped(string stepName, string? reason = null);
}

/// <summary>
/// Interface for workflow lifecycle events.
/// Use this interface for components that track overall workflow progress.
/// </summary>
public interface IWorkflowLogger
{
    /// <summary>Logs that a workflow has started.</summary>
    void WorkflowStarted(string workflowName, string? scriptPath = null, int totalSteps = 0);

    /// <summary>Logs that a workflow has completed.</summary>
    void WorkflowCompleted(string workflowName, TimeSpan duration, int stepsRun, int stepsFailed);
}

/// <summary>
/// Combined interface for full build logging functionality.
/// Extends IMessageLogger, IStepLogger, and IWorkflowLogger.
/// Use this interface when you need complete build logging capabilities.
/// </summary>
public interface IBuildLogger : IMessageLogger, IStepLogger, IWorkflowLogger
{
}
