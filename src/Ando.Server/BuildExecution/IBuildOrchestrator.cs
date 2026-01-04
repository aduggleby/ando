// =============================================================================
// IBuildOrchestrator.cs
//
// Summary: Interface for build execution orchestration.
//
// The orchestrator manages the complete build lifecycle including container
// management, repository cloning, build execution, and artifact collection.
// =============================================================================

namespace Ando.Server.BuildExecution;

/// <summary>
/// Orchestrates build execution.
/// </summary>
public interface IBuildOrchestrator
{
    /// <summary>
    /// Executes a build from start to finish.
    /// </summary>
    /// <param name="buildId">The build ID to execute.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ExecuteBuildAsync(int buildId, CancellationToken cancellationToken);
}
