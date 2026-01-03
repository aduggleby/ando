// =============================================================================
// WorkflowResult.cs
//
// Summary: Result types for workflow and step execution.
//
// These types capture the outcome of build execution, including success status,
// timing information, and any error messages. They're returned by WorkflowRunner
// and can be used for build reporting, CI integration, or result analysis.
//
// Design Decisions:
// - Uses init-only properties for immutability after creation
// - StepResults list allows analysis of individual step outcomes
// - Computed properties (StepsRun, StepsFailed) provide convenient summaries
// - ErrorMessage is optional since successful steps don't have errors
// =============================================================================

namespace Ando.Workflow;

/// <summary>
/// Result of executing a single build step.
/// </summary>
public class StepResult
{
    /// <summary>Step type name (e.g., "Dotnet.Build").</summary>
    public string StepName { get; init; } = "";

    /// <summary>Additional context (e.g., project name).</summary>
    public string? Context { get; init; }

    /// <summary>Whether the step completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Time taken to execute the step.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Error message if the step failed, null on success.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Result of executing the entire workflow (all steps).
/// </summary>
public class WorkflowResult
{
    /// <summary>Workflow name (currently always "build").</summary>
    public string WorkflowName { get; init; } = "";

    /// <summary>Whether all steps completed successfully.</summary>
    public bool Success { get; init; }

    /// <summary>Total time taken for all steps.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>Individual results for each step that was executed.</summary>
    public List<StepResult> StepResults { get; init; } = [];

    /// <summary>Number of steps that were executed (may be less than registered if fail-fast triggered).</summary>
    public int StepsRun => StepResults.Count;

    /// <summary>Number of steps that failed.</summary>
    public int StepsFailed => StepResults.Count(s => !s.Success);
}
