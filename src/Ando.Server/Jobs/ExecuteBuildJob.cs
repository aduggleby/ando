// =============================================================================
// ExecuteBuildJob.cs
//
// Summary: Hangfire job that executes builds.
//
// This is the entry point for build execution from Hangfire. It delegates
// to the BuildOrchestrator for actual build execution.
//
// Design Decisions:
// - No automatic retries (builds should not auto-retry)
// - Runs in the "builds" queue
// - Delegates all logic to BuildOrchestrator
// =============================================================================

using Ando.Server.BuildExecution;
using Hangfire;

namespace Ando.Server.Jobs;

/// <summary>
/// Hangfire job for executing builds.
/// </summary>
public class ExecuteBuildJob
{
    private readonly IBuildOrchestrator _orchestrator;

    public ExecuteBuildJob(IBuildOrchestrator orchestrator)
    {
        _orchestrator = orchestrator;
    }

    /// <summary>
    /// Executes a build.
    /// </summary>
    /// <param name="buildId">The build ID to execute.</param>
    /// <param name="cancellationToken">Cancellation token from Hangfire.</param>
    [Queue("builds")]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(int buildId, CancellationToken cancellationToken)
    {
        await _orchestrator.ExecuteBuildAsync(buildId, cancellationToken);
    }
}
