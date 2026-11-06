// =============================================================================
// StepRegistry.cs
//
// Summary: Interface and implementation for collecting build steps.
//
// StepRegistry is the central collection point for all build steps. Operations
// (Dotnet, Ef, Npm) register steps here during script execution. After script
// loading completes, WorkflowRunner processes all registered steps in order.
//
// Architecture:
// - Single registry per build, created by BuildContext
// - Steps are added to the current scope (the run's top-level scope unless a
//   block, finally or deferred body is being captured)
// - Each scope has its own finally steps, which run after the scope's steps
//   whatever happens (see WorkflowRunner)
// - IStepRegistry interface enables mocking for tests
//
// Design Decisions:
// - Interface allows mocking in unit tests without executing real steps
// - List maintains insertion order for predictable execution
// - Capture() swaps the current scope so the step model can express blocks
//   without changing how operations register their steps
// =============================================================================

namespace Ando.Steps;

/// <summary>
/// Interface for step registration.
/// Operations use this to register build steps without depending on concrete implementation.
/// </summary>
public interface IStepRegistry
{
    /// <summary>
    /// All registered steps in order of registration.
    /// </summary>
    IReadOnlyList<BuildStep> Steps { get; }

    /// <summary>Registers a pre-constructed BuildStep.</summary>
    void Register(BuildStep step);

    /// <summary>
    /// Convenience method to register a step inline.
    /// Creates a BuildStep from the provided parameters.
    /// </summary>
    void Register(string name, Func<Task<bool>> execute, string? context = null);

    /// <summary>
    /// Clears all registered steps.
    /// Used in tests to reset state between test runs.
    /// </summary>
    void Clear();
}

/// <summary>
/// A list of steps plus the finally steps that always run after them.
/// The run itself, each block body and each deferred body is a scope.
/// </summary>
public class StepScope
{
    /// <summary>Steps in registration order.</summary>
    public List<BuildStep> Steps { get; } = [];

    /// <summary>
    /// Finally steps in registration order. They run after <see cref="Steps"/>
    /// in reverse order (last registered first, like nested try/finally).
    /// </summary>
    public List<BuildStep> Finally { get; } = [];
}

/// <summary>
/// Collects build steps registered during script execution.
/// Steps are executed in order by WorkflowRunner after script loading.
/// </summary>
public class StepRegistry : IStepRegistry
{
    private readonly StepScope _root = new();
    private readonly Stack<StepScope> _scopes = new();

    /// <summary>
    /// Top-level steps in order of registration.
    /// Read-only to prevent external modification.
    /// </summary>
    public IReadOnlyList<BuildStep> Steps => _root.Steps;

    /// <summary>The run's top-level scope, including its finally steps.</summary>
    public StepScope Root => _root;

    /// <summary>The scope new steps are added to.</summary>
    public StepScope CurrentScope => _scopes.Count > 0 ? _scopes.Peek() : _root;

    /// <summary>
    /// Default timeout for command steps registered from now on, set by
    /// Ando.CommandTimeout(). Null keeps each operation's own default.
    /// </summary>
    public int? DefaultCommandTimeoutMs { get; set; }

    /// <summary>
    /// Every statically known step, including the steps inside blocks and
    /// finally scopes. Steps registered at runtime by Ando.Defer are not included.
    /// Used by pre-flight checks that look for specific step names.
    /// </summary>
    public IEnumerable<BuildStep> AllSteps => Flatten(_root);

    private static IEnumerable<BuildStep> Flatten(StepScope scope)
    {
        foreach (var step in scope.Steps.Concat(scope.Finally))
        {
            yield return step;
            foreach (var child in step.ChildScopes)
            {
                foreach (var nested in Flatten(child))
                {
                    yield return nested;
                }
            }
        }
    }

    /// <summary>Registers a pre-constructed BuildStep.</summary>
    public void Register(BuildStep step)
    {
        CurrentScope.Steps.Add(step);
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
        CurrentScope.Steps.Add(new BuildStep(name, execute, context));
    }

    /// <summary>
    /// Runs <paramref name="body"/> with a fresh scope as the registration target
    /// and returns that scope with every step the body registered.
    /// </summary>
    public StepScope Capture(Action body)
    {
        var scope = new StepScope();
        _scopes.Push(scope);
        try
        {
            body();
        }
        finally
        {
            _scopes.Pop();
        }

        return scope;
    }

    /// <summary>
    /// Adds a finally step to the current scope.
    /// </summary>
    public void RegisterFinally(BuildStep step)
    {
        CurrentScope.Finally.Add(step);
    }

    /// <summary>
    /// Clears all registered steps.
    /// Used in tests to reset state between test runs.
    /// </summary>
    public void Clear()
    {
        _root.Steps.Clear();
        _root.Finally.Clear();
        _scopes.Clear();
        DefaultCommandTimeoutMs = null;
    }
}
