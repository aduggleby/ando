// =============================================================================
// GetBuildLogsEndpoint.cs
//
// Summary: FastEndpoint for getting build logs (SignalR catch-up).
//
// Returns log entries after a specific sequence number for catching up
// after reconnection to the SignalR hub.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Supports incremental log fetching
// - Returns build status and completion flag
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Builds;
using Ando.Server.Data;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Builds;

/// <summary>
/// GET /api/builds/{id}/logs - Get build logs after sequence.
/// </summary>
public class GetBuildLogsEndpoint : EndpointWithoutRequest<GetBuildLogsResponse>
{
    private readonly AndoDbContext _db;

    public GetBuildLogsEndpoint(AndoDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/builds/{id}/logs");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var buildId = Route<int>("id");
        var afterSequence = Query<int>("afterSequence", isRequired: false);
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var build = await _db.Builds
            .Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == buildId, ct);

        if (build == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        // Verify ownership
        if (build.Project.OwnerId != userId)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var logs = await _db.BuildLogEntries
            .Where(l => l.BuildId == buildId && l.Sequence > afterSequence)
            .OrderBy(l => l.Sequence)
            .Select(l => new LogEntryDto(
                l.Id,
                l.Sequence,
                l.Type.ToString(),
                l.Message,
                l.StepName,
                l.Timestamp
            ))
            .ToListAsync(ct);

        await SendAsync(new GetBuildLogsResponse(
            logs,
            build.Status.ToString(),
            build.Status != BuildStatus.Queued && build.Status != BuildStatus.Running
        ), cancellation: ct);
    }
}
