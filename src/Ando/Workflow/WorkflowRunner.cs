// =============================================================================
// WorkflowRunner.cs
//
// Summary: Executes registered build steps with timing, logging and cleanup.
//
// WorkflowRunner is the execution engine that processes all steps registered
// during script loading. It executes steps sequentially, logs progress, tracks
// timing, stops at the first failure, and then runs finally steps.
//
// Architecture:
// - Receives StepRegistry containing all steps to execute
// - Runs the root scope: its steps in registration order (fail-fast), then its
//   finally steps (always, last registered first)
// - Composite steps (blocks, deferred bodies) run nested scopes through the
//   IStepRunner methods, so nested steps get the same logging and recording
// - Returns WorkflowResult with every step that ran, including nested ones
//
// Design Decisions:
// - Fail-fast: a scope stops at its first failure
// - Finally and on-failure steps ignore cancellation, so Ctrl+C still cleans up
// - Each finally step of a scope runs even when an earlier one failed
// - Exception handling wraps step execution for graceful error reporting
// =============================================================================

using System.Diagnostics;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Workflow;

/// <summary>
/// Executes registered build steps in sequence with timing and logging.
/// Implements fail-fast behavior with finally steps that always run.
/// </summary>
public class WorkflowRunner(StepRegistry registry, IBuildLogger logger) : IStepRunner
{
    private readonly List<StepResult> _stepResults = [];
    private CancellationToken _cancellationToken;
    private bool _wasCancelled;
    private int _depth;
    private bool _continueOnFailure;

    // Scopes currently running, innermost on top (for RunDeferredAsync).
    private readonly Stack<StepScope> _executing = new();
    private int _cleanupDepth;
    private bool _stoppedRunningCommands;
    private (BuildStep Step, DateTimeOffset StartedAt, int Depth, bool Cleanup)? _runningStep;
    private DateTimeOffset _runStartedAt;

    /// <summary>
    /// Called once when a step is interrupted by Ctrl+C, before any on-failure
    /// or finally step runs. The CLI uses it to stop commands still running in
    /// the build container (an interrupted `docker exec` leaves its process
    /// running), so cleanup never races the interrupted command.
    /// </summary>
    public Func<Task>? StopRunningCommandsAsync { get; set; }

    /// <summary>
    /// Executes all registered steps in order, then the run's finally steps.
    /// </summary>
    /// <param name="options">Build options (configuration, etc.).</param>
    /// <param name="scriptPath">Path to build script for logging context.</param>
    /// <param name="cancellationToken">Token to cancel the workflow execution.</param>
    /// <returns>Result containing success status and detailed step results.</returns>
    public async Task<WorkflowResult> RunAsync(
        BuildOptions options,
        string? scriptPath = null,
        CancellationToken cancellationToken = default)
    {
        var workflowStopwatch = Stopwatch.StartNew();
        _runStartedAt = DateTimeOffset.UtcNow;
        _stepResults.Clear();
        _cancellationToken = cancellationToken;
        _wasCancelled = false;
        _depth = 0;
        _cleanupDepth = 0;
        _executing.Clear();
        _stoppedRunningCommands = false;
        _continueOnFailure = false;
        StepCancellation.Current = cancellationToken;
        StepCancellation.SetRun(cancellationToken);

        // Step counters ("[3/12]") only make sense for a flat list of steps.
        var hasNestedSteps = registry.Root.Finally.Count > 0 || registry.Steps.Any(s => s.IsComposite);
        logger.WorkflowStarted("build", scriptPath, hasNestedSteps ? 0 : registry.Steps.Count);

        var overallSuccess = await RunScopeAsync(registry.Root);

        workflowStopwatch.Stop();
        StepCancellation.Current = CancellationToken.None;

        // Log workflow completion with summary.
        logger.WorkflowCompleted(
            "build",
            scriptPath,
            workflowStopwatch.Elapsed,
            _stepResults.Count,
            _stepResults.Count(s => !s.Success));

        return new WorkflowResult
        {
            WorkflowName = "build",
            Success = overallSuccess,
            WasCancelled = _wasCancelled,
            Duration = workflowStopwatch.Elapsed,
            StepResults = [.. _stepResults]
        };
    }

    /// <summary>
    /// The result so far, for a run that is cut short by a forced exit: the steps
    /// that finished, plus the step that was running, marked "aborted".
    /// </summary>
    public WorkflowResult SnapshotAborted()
    {
        List<StepResult> steps;
        (BuildStep Step, DateTimeOffset StartedAt, int Depth, bool Cleanup)? current;
        lock (_stepResults)
        {
            steps = [.. _stepResults];
            current = _runningStep;
        }
        if (current is { } running)
        {
            steps.Add(new StepResult
            {
                StepName = running.Step.Name,
                Context = running.Step.Context,
                Success = false,
                StartedAt = running.StartedAt,
                EndedAt = DateTimeOffset.UtcNow,
                Duration = DateTimeOffset.UtcNow - running.StartedAt,
                Depth = running.Depth,
                Cleanup = running.Cleanup,
                ErrorMessage = "aborted"
            });
        }

        return new WorkflowResult
        {
            WorkflowName = "build",
            Success = false,
            WasCancelled = true,
            Aborted = true,
            Duration = DateTimeOffset.UtcNow - _runStartedAt,
            StepResults = steps
        };
    }

    /// <inheritdoc />
    public async Task<bool> RunScopeAsync(StepScope scope, IReadOnlyList<StepScope>? onFailure = null)
    {
        _executing.Push(scope);
        var previousPolicy = _continueOnFailure;
        _continueOnFailure = false;
        try
        {
            return await RunScopeCoreAsync(scope, onFailure);
        }
        finally
        {
            _continueOnFailure = previousPolicy;
            _executing.Pop();
        }
    }

    /// <inheritdoc />
    public async Task<bool> RunDeferredAsync(StepScope deferred)
    {
        // Finally steps registered by a deferred body belong to the scope that is
        // running it (the enclosing block or the run), not to the body itself:
        // a TemporaryAccessRule added inside Ando.When stays until the block ends.
        if (deferred.Finally.Count > 0 && _executing.Count > 0)
        {
            _executing.Peek().Finally.AddRange(deferred.Finally);
            deferred.Finally.Clear();
        }

        // Inside a finally scope, a deferred body keeps that scope's policy:
        // one failing cleanup step does not skip the rest.
        return await RunStepsAsync(deferred.Steps, _continueOnFailure);
    }

    private async Task<bool> RunScopeCoreAsync(StepScope scope, IReadOnlyList<StepScope>? onFailure)
    {
        var success = await RunStepsAsync(scope.Steps, continueOnFailure: false);

        if (!success && onFailure != null)
        {
            foreach (var handler in onFailure)
            {
                await RunCleanupScopeAsync(handler, continueOnFailure: false);
            }
        }

        var finallySuccess = await RunFinallyAsync(scope);
        return success && finallySuccess;
    }

    /// <inheritdoc />
    public async Task<bool> RunCleanupScopeAsync(StepScope scope, bool continueOnFailure)
    {
        EnterCleanup();
        _executing.Push(scope);
        var previousPolicy = _continueOnFailure;
        _continueOnFailure = continueOnFailure;
        try
        {
            var success = await RunStepsAsync(scope.Steps, continueOnFailure);
            var finallySuccess = await RunFinallyAsync(scope);
            return success && finallySuccess;
        }
        finally
        {
            _continueOnFailure = previousPolicy;
            _executing.Pop();
            ExitCleanup();
        }
    }

    // Runs a scope's finally steps, last registered first. Every one runs, even
    // after a failure or cancellation.
    private async Task<bool> RunFinallyAsync(StepScope scope)
    {
        if (scope.Finally.Count == 0)
        {
            return true;
        }

        EnterCleanup();
        try
        {
            var success = true;
            for (var i = scope.Finally.Count - 1; i >= 0; i--)
            {
                if (!await RunStepAsync(scope.Finally[i]))
                {
                    success = false;
                }
            }

            return success;
        }
        finally
        {
            ExitCleanup();
        }
    }

    // Cleanup (on-failure and finally steps) ignores cancellation, gets no
    // cancellation token, and its commands are shielded from Ctrl+C.
    private void EnterCleanup()
    {
        _cleanupDepth++;
        Execution.CommandExecutorBase.ShieldNewProcessesFromCtrlC = true;
        StepCancellation.Current = CancellationToken.None;
    }

    private void ExitCleanup()
    {
        _cleanupDepth--;
        if (_cleanupDepth == 0)
        {
            // After a Ctrl+C every later command stays shielded.
            Execution.CommandExecutorBase.ShieldNewProcessesFromCtrlC = _cancellationToken.IsCancellationRequested;
            StepCancellation.Current = _cancellationToken;
        }
    }

    private bool InCleanup => _cleanupDepth > 0;

    private async Task<bool> RunStepsAsync(IReadOnlyList<BuildStep> steps, bool continueOnFailure)
    {
        var success = true;

        // Iterate by index: deferred bodies are captured into their own scope,
        // so this list does not change while it runs.
        for (var i = 0; i < steps.Count; i++)
        {
            // Check for cancellation before each step. Cleanup steps ignore it.
            if (!InCleanup && _cancellationToken.IsCancellationRequested)
            {
                if (!_wasCancelled)
                {
                    logger.Warning("Build cancelled by user.");
                }
                _wasCancelled = true;
                return false;
            }

            if (!await RunStepAsync(steps[i]))
            {
                success = false;
                if (!continueOnFailure)
                {
                    break; // Fail-fast: don't continue after failure
                }
            }
        }

        return success;
    }

    private async Task<bool> RunStepAsync(BuildStep step)
    {
        var startedAt = DateTimeOffset.UtcNow;

        // Handle log steps specially - single line output, no completion message.
        if (step.IsLogStep)
        {
            logger.LogStep(step.LogLevel.ToString(), step.LogMessage ?? "");
            Record(step, true, TimeSpan.Zero, startedAt);
            return true;
        }

        // Composite steps log their nested steps, not themselves.
        if (step.ExecuteWithRunner != null)
        {
            _depth++;
            try
            {
                return await step.ExecuteWithRunner(this);
            }
            catch (Exception ex)
            {
                // A deferred body threw while registering its steps.
                logger.StepFailed(step.Name, TimeSpan.Zero, ex.Message);
                Record(step, false, TimeSpan.Zero, startedAt, ex.Message, ex);
                return false;
            }
            finally
            {
                _depth--;
            }
        }

        var stepStopwatch = Stopwatch.StartNew();
        logger.StepStarted(step.Name, step.Context);
        lock (_stepResults) { _runningStep = (step, startedAt, _depth, InCleanup); }

        try
        {
            // Execute the step and check result.
            var success = await step.Execute();
            stepStopwatch.Stop();

            // Ctrl+C reaches the step's process too, and an interrupted
            // `docker exec` can exit 0. A step that was running when the build
            // was cancelled did not finish its work, so never count it as passed.
            if (!InCleanup && _cancellationToken.IsCancellationRequested)
            {
                _wasCancelled = true;
                logger.StepFailed(step.Name, stepStopwatch.Elapsed, "Cancelled by user");
                Record(step, false, stepStopwatch.Elapsed, startedAt, "Cancelled by user");
                await StopRunningCommandsOnceAsync();
                return false;
            }

            if (success)
            {
                logger.StepCompleted(step.Name, stepStopwatch.Elapsed, step.Context);
                Record(step, true, stepStopwatch.Elapsed, startedAt);
                return true;
            }

            // Step returned false - explicit failure.
            logger.StepFailed(step.Name, stepStopwatch.Elapsed, "Step returned false");
            Record(step, false, stepStopwatch.Elapsed, startedAt, "Step returned false");

            // Check if this is an Azure-related step and provide helpful instructions
            CheckAndLogToolAvailability(step.Name);
            return false;
        }
        catch (OperationCanceledException) when (_cancellationToken.IsCancellationRequested)
        {
            // Cancellation requested during step execution.
            stepStopwatch.Stop();
            _wasCancelled = true;
            logger.Warning($"Step '{step.Name}' cancelled.");
            Record(step, false, stepStopwatch.Elapsed, startedAt, "Cancelled by user");
            await StopRunningCommandsOnceAsync();
            return false;
        }
        catch (Exception ex)
        {
            // Step threw an exception - wrap in result with full exception details.
            stepStopwatch.Stop();
            logger.StepFailed(step.Name, stepStopwatch.Elapsed, ex.Message);
            Record(step, false, stepStopwatch.Elapsed, startedAt, ex.Message, ex);

            // Check if this is an Azure-related step and provide helpful instructions
            CheckAndLogToolAvailability(step.Name);
            return false;
        }
    }

    private async Task StopRunningCommandsOnceAsync()
    {
        if (_stoppedRunningCommands || StopRunningCommandsAsync == null)
        {
            return;
        }

        _stoppedRunningCommands = true;
        try
        {
            await StopRunningCommandsAsync();
        }
        catch (Exception ex)
        {
            logger.Warning($"Could not stop commands still running in the build container: {ex.Message}");
        }
    }

    private void Record(
        BuildStep step,
        bool success,
        TimeSpan duration,
        DateTimeOffset startedAt,
        string? error = null,
        Exception? exception = null)
    {
        lock (_stepResults) { _runningStep = null; }
        lock (_stepResults) _stepResults.Add(new StepResult
        {
            StepName = step.Name,
            Context = step.Context,
            Success = success,
            Duration = duration,
            StartedAt = startedAt,
            EndedAt = DateTimeOffset.UtcNow,
            Depth = _depth,
            Cleanup = InCleanup,
            ErrorMessage = error,
            Exception = exception
        });
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
