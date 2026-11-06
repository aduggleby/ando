// =============================================================================
// StepBlock.cs
//
// Summary: A named group of steps with on-failure and finally steps.
//
// Build scripts register steps first and run them later, so C# try/catch/finally
// around step calls has no effect at execution time. Ando.Block gives the same
// structure in the step model:
//
//   Ando.Block("eu", () => { ...steps... })
//       .OnFailure(() => { ...steps run when a block step failed... })
//       .Finally(() => { ...steps that always run... });
//
// Design Decisions:
// - The body runs immediately (at evaluation time) to capture its steps
// - OnFailure/Finally capture their steps when called, before the run starts
// - A failed block still fails the build after its on-failure steps ran
// =============================================================================

using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// A named group of steps with optional on-failure and finally steps.
/// Created by <see cref="AndoOperations.Block"/>.
/// </summary>
public class StepBlock
{
    private readonly StepRegistry _registry;
    private readonly StepScope _body;
    private readonly List<StepScope> _onFailure = [];
    private readonly List<StepScope> _children;

    internal StepBlock(StepRegistry registry, string name, StepScope body)
    {
        _registry = registry;
        _body = body;
        _children = [body];
        Name = name;
    }

    /// <summary>Block name, shown in logs.</summary>
    public string Name { get; }

    internal StepScope Body => _body;

    internal IReadOnlyList<StepScope> OnFailureScopes => _onFailure;

    // Body plus on-failure scopes, kept live for StepRegistry.AllSteps.
    internal IReadOnlyList<StepScope> ChildScopes => _children;

    /// <summary>
    /// Steps that run only when a step in the block failed or the run was
    /// cancelled, before the block's finally steps. The build still fails.
    /// A failing on-failure step stops the remaining on-failure steps of this call.
    /// Can be called more than once; handlers run in the order they were added.
    /// </summary>
    public StepBlock OnFailure(Action body)
    {
        var scope = _registry.Capture(body);
        _onFailure.Add(scope);
        _children.Add(scope);
        return this;
    }

    /// <summary>
    /// Steps that always run when the block ends: after success, after a failure
    /// (following the on-failure steps) and after Ctrl+C. Every step runs even if
    /// an earlier one failed. Same as calling Ando.Finally inside the block.
    /// </summary>
    public StepBlock Finally(Action body)
    {
        _body.Finally.Add(AndoOperations.CreateFinallyStep(_registry, body));
        return this;
    }
}
