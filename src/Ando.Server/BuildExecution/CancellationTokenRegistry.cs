// =============================================================================
// CancellationTokenRegistry.cs
//
// Summary: Thread-safe registry for build cancellation tokens.
//
// Maps build IDs to CancellationTokenSources, allowing in-progress builds
// to be cancelled from the UI. The registry is a singleton service.
//
// Design Decisions:
// - Uses ConcurrentDictionary for thread safety
// - BuildOrchestrator registers tokens when builds start
// - BuildService calls TryCancel to request cancellation
// =============================================================================

using System.Collections.Concurrent;

namespace Ando.Server.BuildExecution;

/// <summary>
/// Thread-safe registry for build cancellation tokens.
/// </summary>
public class CancellationTokenRegistry
{
    private readonly ConcurrentDictionary<int, CancellationTokenSource> _tokens = new();

    /// <summary>
    /// Registers a cancellation token source for a build.
    /// </summary>
    /// <param name="buildId">The build ID.</param>
    /// <param name="cts">The cancellation token source.</param>
    public void Register(int buildId, CancellationTokenSource cts)
    {
        _tokens[buildId] = cts;
    }

    /// <summary>
    /// Unregisters a cancellation token source when a build completes.
    /// </summary>
    /// <param name="buildId">The build ID.</param>
    public void Unregister(int buildId)
    {
        _tokens.TryRemove(buildId, out _);
    }

    /// <summary>
    /// Attempts to cancel a build.
    /// </summary>
    /// <param name="buildId">The build ID to cancel.</param>
    /// <returns>True if the build was found and cancellation was requested.</returns>
    public bool TryCancel(int buildId)
    {
        if (_tokens.TryGetValue(buildId, out var cts))
        {
            cts.Cancel();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks if a build is currently registered (running).
    /// </summary>
    public bool IsRunning(int buildId)
    {
        return _tokens.ContainsKey(buildId);
    }
}
