// =============================================================================
// RetryBuildEndpoint.cs
//
// Summary: FastEndpoint for retrying a failed, cancelled, or timed out build.
//
// Creates a new build with the same parameters as the original.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Only works for Failed, Cancelled, or TimedOut builds
// - Creates a new build (doesn't modify original)
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
/// POST /api/builds/{id}/retry - Retry a failed build.
/// </summary>
public class RetryBuildEndpoint : EndpointWithoutRequest<RetryBuildResponse>
{
    private readonly AndoDbContext _db;
    private readonly IBuildService _buildService;

    public RetryBuildEndpoint(AndoDbContext db, IBuildService buildService)
    {
        _db = db;
        _buildService = buildService;
    }

    public override void Configure()
    {
        Post("/builds/{id}/retry");
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

        if (build.Status != BuildStatus.Failed &&
            build.Status != BuildStatus.Cancelled &&
            build.Status != BuildStatus.TimedOut)
        {
            await SendAsync(new RetryBuildResponse(false, Error: "Build cannot be retried in its current state."), cancellation: ct);
            return;
        }

        // Create a new build with same parameters
        var newBuildId = await _buildService.QueueBuildAsync(
            build.ProjectId,
            build.CommitSha,
            build.Branch,
            BuildTrigger.Manual,
            build.CommitMessage,
            build.CommitAuthor,
            build.PullRequestNumber);

        await SendAsync(new RetryBuildResponse(true, newBuildId), cancellation: ct);
    }
}
