// =============================================================================
// BuildStep.cs
//
// Summary: Represents a single executable step in the build workflow.
//
// BuildStep encapsulates a named operation that can be executed by the
// WorkflowRunner. Steps are registered during script execution and run
// sequentially afterward.
//
// Architecture:
// - Name identifies the step type (e.g., "Dotnet.Build")
// - Context provides additional info (e.g., project name) for logging
// - Execute is the async action that performs the actual work
// - DisplayName combines Name and Context for readable log output
// - Log steps are special steps that render as a single line
//
// Design Decisions:
// - Execute returns Task<bool> to indicate success/failure
// - Context is optional for steps that don't need additional identification
// - Immutable after construction for thread safety
// - Log steps have IsLogStep=true and display message inline
// =============================================================================

namespace Ando.Steps;

/// <summary>
/// Log level for log steps.
/// </summary>
public enum LogStepLevel
{
    Info,
    Warning,
    Error,
    Debug
}

/// <summary>
/// Represents a single executable step in the build workflow.
/// Steps are registered during script execution and run by WorkflowRunner.
/// </summary>
/// <param name="Name">Step type name (e.g., "Dotnet.Build", "Npm.Install").</param>
/// <param name="Execute">Async function that executes the step and returns success/failure.</param>
/// <param name="Context">Additional context (e.g., project name) shown in logs.</param>
public class BuildStep(string Name, Func<Task<bool>> Execute, string? Context = null)
{
    /// <summary>Step type name (e.g., "Dotnet.Build", "Npm.Install").</summary>
    public string Name { get; } = Name;

    /// <summary>Additional context (e.g., project name) shown in logs.</summary>
    public string? Context { get; } = Context;

    /// <summary>Async function that executes the step and returns success/failure.</summary>
    public Func<Task<bool>> Execute { get; } = Execute;

    /// <summary>
    /// Human-readable name for logging.
    /// Format: "Name" or "Name (Context)" if context is present.
    /// </summary>
    public string DisplayName => Context != null ? $"{Name} ({Context})" : Name;

    /// <summary>
    /// Whether this is a log step that renders as a single line.
    /// </summary>
    public bool IsLogStep { get; init; }

    /// <summary>
    /// Log level for log steps (Info, Warning, Error, Debug).
    /// </summary>
    public LogStepLevel LogLevel { get; init; }

    /// <summary>
    /// Message for log steps.
    /// </summary>
    public string? LogMessage { get; init; }
}
