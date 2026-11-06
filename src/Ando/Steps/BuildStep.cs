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

    /// <summary>
    /// For composite steps (blocks, finally scopes, deferred bodies): runs the
    /// nested steps through the workflow runner instead of <see cref="Execute"/>.
    /// The runner runs nested steps with the same logging, recording and
    /// cancellation handling as top-level steps.
    /// </summary>
    public Func<IStepRunner, Task<bool>>? ExecuteWithRunner { get; init; }

    /// <summary>
    /// Statically known nested scopes of a composite step, used by pre-flight
    /// checks that scan every step (see StepRegistry.AllSteps).
    /// </summary>
    public IReadOnlyList<StepScope> ChildScopes { get; init; } = [];

    /// <summary>Whether this is a composite step that runs nested steps.</summary>
    public bool IsComposite => ExecuteWithRunner != null;
}

/// <summary>
/// Runs nested steps for composite steps (see <see cref="BuildStep.ExecuteWithRunner"/>).
/// Implemented by WorkflowRunner.
/// </summary>
public interface IStepRunner
{
    /// <summary>
    /// Runs the scope's steps (stopping at the first failure). If one failed or
    /// the run was cancelled, runs <paramref name="onFailure"/> scopes as cleanup.
    /// Then runs the scope's finally steps (every one of them, last registered
    /// first, even after a failure or cancellation). Returns true only when the
    /// scope's steps and finally steps all succeeded; on-failure steps never turn
    /// a failure into success.
    /// </summary>
    Task<bool> RunScopeAsync(StepScope scope, IReadOnlyList<StepScope>? onFailure = null);

    /// <summary>
    /// Runs a scope that must run even after a failure or cancellation (finally
    /// and on-failure steps): cancellation is ignored for everything inside it.
    /// When <paramref name="continueOnFailure"/> is true, a failing step does not
    /// stop the remaining steps of this scope.
    /// </summary>
    Task<bool> RunCleanupScopeAsync(StepScope scope, bool continueOnFailure);

    /// <summary>
    /// Runs the steps a deferred body (Ando.Defer, Ando.When) registered at
    /// execution time. Finally steps it registered move to the scope that is
    /// running it, so they run when the enclosing block (or the run) ends.
    /// </summary>
    Task<bool> RunDeferredAsync(StepScope deferred);
}
