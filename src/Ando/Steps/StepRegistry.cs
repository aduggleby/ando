// =============================================================================
// StepRegistry.cs
//
// Summary: Collects build steps registered during script execution.
//
// StepRegistry is the central collection point for all build steps. Operations
// (Dotnet, Ef, Npm) register steps here during script execution. After script
// loading completes, WorkflowRunner processes all registered steps in order.
//
// Architecture:
// - Single registry per build, created by BuildContext
// - Steps are added in registration order (order of calls in script)
// - WorkflowRunner reads Steps and executes them sequentially
// - Clear() enables registry reuse in tests
//
// Design Decisions:
// - List maintains insertion order for predictable execution
// - IReadOnlyList prevents external modification while allowing iteration
// - Convenience overload creates BuildStep inline to reduce boilerplate
// =============================================================================

namespace Ando.Steps;

/// <summary>
/// Collects build steps registered during script execution.
/// Steps are executed in order by WorkflowRunner after script loading.
/// </summary>
public class StepRegistry
{
    private readonly List<BuildStep> _steps = [];

    /// <summary>
    /// All registered steps in order of registration.
    /// Read-only to prevent external modification.
    /// </summary>
    public IReadOnlyList<BuildStep> Steps => _steps;

    /// <summary>Registers a pre-constructed BuildStep.</summary>
    public void Register(BuildStep step)
    {
        _steps.Add(step);
    }

    /// <summary>
    /// Convenience method to register a step inline.
    /// Creates a BuildStep from the provided parameters.
    /// </summary>
    /// <param name="name">Step type name (e.g., "Dotnet.Build").</param>
    /// <param name="execute">Async function that performs the step.</param>
    /// <param name="context">Optional context (e.g., project name) for logging.</param>
    public void Register(string name, Func<Task<bool>> execute, string? context = null)
    {
        _steps.Add(new BuildStep(name, execute, context));
    }

    /// <summary>
    /// Clears all registered steps.
    /// Used in tests to reset state between test runs.
    /// </summary>
    public void Clear()
    {
        _steps.Clear();
    }
}
