// =============================================================================
// BuildLogHub.cs
//
// Summary: SignalR hub for real-time build log streaming.
//
// Clients connect to this hub to receive live build log updates. Each build
// has a group that clients join to receive logs for that specific build.
//
// Design Decisions:
// - Group-based broadcasting for efficient message routing
// - Clients join/leave groups based on which build they're viewing
// - No authentication on hub methods (auth handled at connection level)
// =============================================================================

using Microsoft.AspNetCore.SignalR;

namespace Ando.Server.Hubs;

/// <summary>
/// SignalR hub for streaming build logs in real-time.
/// </summary>
public class BuildLogHub : Hub
{
    /// <summary>
    /// Join the log stream for a specific build.
    /// </summary>
    /// <param name="buildId">The build ID to subscribe to.</param>
    public async Task JoinBuildLog(int buildId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(buildId));
    }

    /// <summary>
    /// Leave the log stream for a specific build.
    /// </summary>
    /// <param name="buildId">The build ID to unsubscribe from.</param>
    public async Task LeaveBuildLog(int buildId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(buildId));
    }

    /// <summary>
    /// Gets the SignalR group name for a build.
    /// </summary>
    public static string GetGroupName(int buildId) => $"build-{buildId}";
}
