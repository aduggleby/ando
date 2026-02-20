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
using System.Security.Claims;
using Ando.Server.Models;

namespace Ando.Server.Hubs;

/// <summary>
/// SignalR hub for streaming build logs in real-time.
/// </summary>
public class BuildLogHub : Hub
{
    private readonly ILogger<BuildLogHub> _logger;

    public BuildLogHub(ILogger<BuildLogHub> logger)
    {
        _logger = logger;
    }

    public override Task OnConnectedAsync()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            _ = Groups.AddToGroupAsync(Context.ConnectionId, GetUserGroupName(userId));
        }

        if (Context.User?.IsInRole(UserRoles.Admin) == true)
        {
            _ = Groups.AddToGroupAsync(Context.ConnectionId, GetAdminsGroupName());
        }

        _logger.LogInformation(
            "SignalR connected: connectionId={ConnectionId} user={User} auth={IsAuthenticated}",
            Context.ConnectionId,
            Context.User?.Identity?.Name ?? "(anonymous)",
            Context.User?.Identity?.IsAuthenticated == true);

        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        if (exception != null)
        {
            _logger.LogWarning(
                exception,
                "SignalR disconnected with error: connectionId={ConnectionId} user={User} auth={IsAuthenticated}",
                Context.ConnectionId,
                Context.User?.Identity?.Name ?? "(anonymous)",
                Context.User?.Identity?.IsAuthenticated == true);
        }
        else
        {
            _logger.LogInformation(
                "SignalR disconnected: connectionId={ConnectionId} user={User} auth={IsAuthenticated}",
                Context.ConnectionId,
                Context.User?.Identity?.Name ?? "(anonymous)",
                Context.User?.Identity?.IsAuthenticated == true);
        }

        return base.OnDisconnectedAsync(exception);
    }

    /// <summary>
    /// Join the log stream for a specific build.
    /// </summary>
    /// <param name="buildId">The build ID to subscribe to.</param>
    public async Task JoinBuildLog(int buildId)
    {
        _logger.LogInformation(
            "SignalR join build logs: connectionId={ConnectionId} buildId={BuildId} user={User} auth={IsAuthenticated}",
            Context.ConnectionId,
            buildId,
            Context.User?.Identity?.Name ?? "(anonymous)",
            Context.User?.Identity?.IsAuthenticated == true);

        await Groups.AddToGroupAsync(Context.ConnectionId, GetGroupName(buildId));
    }

    /// <summary>
    /// Leave the log stream for a specific build.
    /// </summary>
    /// <param name="buildId">The build ID to unsubscribe from.</param>
    public async Task LeaveBuildLog(int buildId)
    {
        _logger.LogInformation(
            "SignalR leave build logs: connectionId={ConnectionId} buildId={BuildId} user={User} auth={IsAuthenticated}",
            Context.ConnectionId,
            buildId,
            Context.User?.Identity?.Name ?? "(anonymous)",
            Context.User?.Identity?.IsAuthenticated == true);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GetGroupName(buildId));
    }

    /// <summary>
    /// Gets the SignalR group name for a build.
    /// </summary>
    public static string GetGroupName(int buildId) => $"build-{buildId}";

    /// <summary>
    /// Gets the SignalR group name for a user.
    /// </summary>
    public static string GetUserGroupName(int userId) => $"user-{userId}";

    /// <summary>
    /// Gets the SignalR group name for admin users.
    /// </summary>
    public static string GetAdminsGroupName() => "admins";
}
