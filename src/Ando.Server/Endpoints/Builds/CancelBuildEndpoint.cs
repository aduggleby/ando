// =============================================================================
// CancelBuildEndpoint.cs
//
// Summary: FastEndpoint for cancelling a running or queued build.
//
// Cancels the build execution and updates the build status.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Only works for Queued or Running builds
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Builds;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Builds;

/// <summary>
/// POST /api/builds/{id}/cancel - Cancel a build.
/// </summary>
public class CancelBuildEndpoint : EndpointWithoutRequest<CancelBuildResponse>
{
    private readonly AndoDbContext _db;
    private readonly IBuildService _buildService;

    public CancelBuildEndpoint(AndoDbContext db, IBuildService buildService)
    {
        _db = db;
        _buildService = buildService;
    }

    public override void Configure()
    {
        Post("/builds/{id}/cancel");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var buildId = Route<int>("id");
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

        if (build.Status != BuildStatus.Queued && build.Status != BuildStatus.Running)
        {
            await SendAsync(new CancelBuildResponse(false, "Build cannot be cancelled in its current state."), cancellation: ct);
            return;
        }

        var success = await _buildService.CancelBuildAsync(buildId);

        if (success)
        {
            await SendAsync(new CancelBuildResponse(true), cancellation: ct);
        }
        else
        {
            await SendAsync(new CancelBuildResponse(false, "Failed to cancel build."), cancellation: ct);
        }
    }
}
