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
//
// Design Decisions:
// - Execute returns Task<bool> to indicate success/failure
// - Context is optional for steps that don't need additional identification
// - Immutable after construction for thread safety
// =============================================================================

namespace Ando.Steps;

/// <summary>
/// Represents a single executable step in the build workflow.
/// Steps are registered during script execution and run by WorkflowRunner.
/// </summary>
public class BuildStep
{
    /// <summary>Step type name (e.g., "Dotnet.Build", "Npm.Install").</summary>
    public string Name { get; }

    /// <summary>Additional context (e.g., project name) shown in logs.</summary>
    public string? Context { get; }

    /// <summary>Async function that executes the step and returns success/failure.</summary>
    public Func<Task<bool>> Execute { get; }

    public BuildStep(string name, Func<Task<bool>> execute, string? context = null)
    {
        Name = name;
        Context = context;
        Execute = execute;
    }

    /// <summary>
    /// Human-readable name for logging.
    /// Format: "Name" or "Name (Context)" if context is present.
    /// </summary>
    public string DisplayName => Context != null ? $"{Name} ({Context})" : Name;
}
