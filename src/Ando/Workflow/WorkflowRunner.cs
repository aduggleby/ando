// =============================================================================
// WorkflowRunner.cs
//
// Summary: Executes registered build steps in sequence with timing and logging.
//
// WorkflowRunner is the execution engine that processes all steps registered
// during script loading. It executes steps sequentially, logs progress, tracks
// timing, and implements fail-fast behavior on errors.
//
// Architecture:
// - Receives StepRegistry containing all steps to execute
// - Runs steps in registration order (sequential execution)
// - Tracks timing for both individual steps and total workflow
// - Reports progress via IBuildLogger interface
// - Returns WorkflowResult with detailed execution information
//
// Design Decisions:
// - Fail-fast: stops on first failure rather than continuing
// - Sequential execution: predictable ordering, no parallelism complexity
// - Separate stopwatches for step and workflow timing accuracy
// - Exception handling wraps step execution for graceful error reporting
// =============================================================================

using System.Diagnostics;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Workflow;

/// <summary>
/// Executes registered build steps in sequence with timing and logging.
/// Implements fail-fast behavior: stops on first step failure.
/// </summary>
public class WorkflowRunner(StepRegistry registry, IBuildLogger logger)
{
    /// <summary>
    /// Executes all registered steps in order.
    /// </summary>
    /// <param name="options">Build options (configuration, etc.).</param>
    /// <param name="scriptPath">Path to build script for logging context.</param>
    /// <returns>Result containing success status and detailed step results.</returns>
    public async Task<WorkflowResult> RunAsync(BuildOptions options, string? scriptPath = null)
    {
        var workflowStopwatch = Stopwatch.StartNew();
        var stepResults = new List<StepResult>();
        var overallSuccess = true;

        // Log workflow start with total step count for progress tracking.
        logger.WorkflowStarted("build", scriptPath, registry.Steps.Count);

        // Execute each step sequentially.
        foreach (var step in registry.Steps)
        {
            // Handle log steps specially - single line output, no completion message.
            if (step.IsLogStep)
            {
                var levelName = step.LogLevel.ToString();
                logger.LogStep(levelName, step.LogMessage ?? "");

                // Log steps always succeed and have no execution.
                stepResults.Add(new StepResult
                {
                    StepName = step.Name,
                    Context = step.Context,
                    Success = true,
                    Duration = TimeSpan.Zero
                });
                continue;
            }

            var stepStopwatch = Stopwatch.StartNew();
            logger.StepStarted(step.Name, step.Context);

            try
            {
                // Execute the step and check result.
                var success = await step.Execute();
                stepStopwatch.Stop();

                if (success)
                {
                    logger.StepCompleted(step.Name, stepStopwatch.Elapsed, step.Context);
                    stepResults.Add(new StepResult
                    {
                        StepName = step.Name,
                        Context = step.Context,
                        Success = true,
                        Duration = stepStopwatch.Elapsed
                    });
                }
                else
                {
                    // Step returned false - explicit failure.
                    logger.StepFailed(step.Name, stepStopwatch.Elapsed, "Step returned false");
                    stepResults.Add(new StepResult
                    {
                        StepName = step.Name,
                        Context = step.Context,
                        Success = false,
                        Duration = stepStopwatch.Elapsed,
                        ErrorMessage = "Step returned false"
                    });

                    // Check if this is an Azure-related step and provide helpful instructions
                    CheckAndLogToolAvailability(step.Name);

                    overallSuccess = false;
                    break; // Fail-fast: don't continue after failure
                }
            }
            catch (Exception ex)
            {
                // Step threw an exception - wrap in result with full exception details.
                stepStopwatch.Stop();
                logger.StepFailed(step.Name, stepStopwatch.Elapsed, ex.Message);
                stepResults.Add(new StepResult
                {
                    StepName = step.Name,
                    Context = step.Context,
                    Success = false,
                    Duration = stepStopwatch.Elapsed,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });

                // Check if this is an Azure-related step and provide helpful instructions
                CheckAndLogToolAvailability(step.Name);

                overallSuccess = false;
                break; // Fail-fast: don't continue after exception
            }
        }

        workflowStopwatch.Stop();

        // Log workflow completion with summary.
        logger.WorkflowCompleted(
            "build",
            scriptPath,
            workflowStopwatch.Elapsed,
            stepResults.Count,
            stepResults.Count(s => !s.Success));

        return new WorkflowResult
        {
            WorkflowName = "build",
            Success = overallSuccess,
            Duration = workflowStopwatch.Elapsed,
            StepResults = stepResults
        };
    }

    /// <summary>
    /// Checks if a failed step requires a tool that isn't installed and logs helpful instructions.
    /// Uses the ToolAvailabilityRegistry to find the appropriate checker for the step.
    /// </summary>
    private void CheckAndLogToolAvailability(string stepName)
    {
        var checker = ToolAvailabilityRegistry.FindChecker(stepName);
        if (checker != null && !checker.IsAvailable())
        {
            logger.Error("");
            logger.Error($"Required tool is not installed. To install:");
            logger.Error(checker.GetInstallInstructions());
            logger.Error("");
            logger.Error($"Or visit: {checker.GetDocumentationUrl()}");
        }
    }
}
