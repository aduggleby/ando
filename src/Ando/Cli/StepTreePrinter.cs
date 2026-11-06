// =============================================================================
// StepTreePrinter.cs
//
// Summary: Renders the registered step tree for `ando run --dry-run`.
//
// The script is evaluated and its steps registered as usual, but nothing runs.
// The tree shows what would run, in order. Bodies of Ando.When and Ando.Defer
// are C# that only runs at execution time, so they appear as a single line.
//
// Design Decisions:
// - Pure function from StepScope to lines, so it is unit-testable
// - Finally steps are listed in execution order (last registered first)
// =============================================================================

using Ando.Operations;
using Ando.Steps;

namespace Ando.Cli;

/// <summary>
/// Renders a registered step tree as indented text lines.
/// </summary>
public static class StepTreePrinter
{
    private const string Indent = "  ";

    /// <summary>Renders the scope's steps, on-failure scopes and finally steps.</summary>
    public static IReadOnlyList<string> Render(StepScope root)
    {
        var lines = new List<string>();
        RenderScope(root, 0, lines);
        return lines;
    }

    private static void RenderScope(StepScope scope, int depth, List<string> lines)
    {
        foreach (var step in scope.Steps)
        {
            RenderStep(step, depth, lines);
        }

        if (scope.Finally.Count > 0)
        {
            lines.Add($"{Pad(depth)}finally (runs last-registered first):");
            foreach (var step in Enumerable.Reverse(scope.Finally))
            {
                // A finally step wraps the steps of its body; show those directly.
                if (step.ChildScopes.Count > 0)
                {
                    foreach (var child in step.ChildScopes)
                    {
                        RenderScope(child, depth + 1, lines);
                    }
                }
                else
                {
                    RenderStep(step, depth + 1, lines);
                }
            }
        }
    }

    private static void RenderStep(BuildStep step, int depth, List<string> lines)
    {
        if (step.Name == "Ando.Block" && step.ChildScopes.Count > 0)
        {
            lines.Add($"{Pad(depth)}Block: {step.Context}");
            RenderBlockBody(step.ChildScopes, depth + 1, lines);
            return;
        }

        if (step.Name == "Ando.Defer")
        {
            lines.Add(Pad(depth) + (step.Context == AndoOperations.WhenContext
                ? "When (decided at run time)"
                : "Defer (runs C# at run time)"));
            return;
        }

        lines.Add(Pad(depth) + step.DisplayName);
        foreach (var child in step.ChildScopes)
        {
            RenderScope(child, depth + 1, lines);
        }
    }

    // A block's first child scope is its body; the others are on-failure scopes.
    // The body's finally steps are shown last, as they run after the on-failure steps.
    private static void RenderBlockBody(IReadOnlyList<StepScope> scopes, int depth, List<string> lines)
    {
        var body = scopes[0];
        foreach (var step in body.Steps)
        {
            RenderStep(step, depth, lines);
        }

        foreach (var onFailure in scopes.Skip(1))
        {
            lines.Add($"{Pad(depth)}on failure:");
            RenderScope(onFailure, depth + 1, lines);
        }

        var finallyOnly = new StepScope();
        finallyOnly.Finally.AddRange(body.Finally);
        RenderScope(finallyOnly, depth, lines);
    }

    private static string Pad(int depth) => string.Concat(Enumerable.Repeat(Indent, depth));
}
