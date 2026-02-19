// =============================================================================
// GetDashboardEndpoint.cs
//
// Summary: FastEndpoint for getting user dashboard data.
//
// Returns recent builds, project count, and build statistics for the
// authenticated user's projects.
//
// Design Decisions:
// - Requires authentication
// - Data is scoped to user's projects only
// - Returns summary statistics for quick overview
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Home;
using Ando.Server.Data;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Home;

/// <summary>
/// GET /api/dashboard - Get user dashboard data.
/// </summary>
public class GetDashboardEndpoint : EndpointWithoutRequest<GetDashboardResponse>
{
    private readonly AndoDbContext _db;

    public GetDashboardEndpoint(AndoDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/dashboard");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var today = DateTime.UtcNow.Date;

        // Get user's project IDs
        var projectIds = await _db.Projects
            .Where(p => p.OwnerId == userId)
            .Select(p => p.Id)
            .ToListAsync(ct);

        // Get recent builds across all user's projects
        var recentBuilds = await _db.Builds
            .Include(b => b.Project)
            .Where(b => projectIds.Contains(b.ProjectId))
            .OrderByDescending(b => b.QueuedAt)
            .Take(10)
            .Select(b => new RecentBuildItemDto(
                b.Id,
                b.Project.RepoFullName,
                b.Branch,
                b.CommitSha.Length >= 8 ? b.CommitSha.Substring(0, 8) : b.CommitSha,
                b.GitVersionTag,
                b.Status.ToString(),
                b.StartedAt,
                b.Duration))
            .ToListAsync(ct);

        // Get statistics
        var totalProjects = projectIds.Count;

        var buildsToday = await _db.Builds
            .Where(b => projectIds.Contains(b.ProjectId))
            .Where(b => b.QueuedAt >= today)
            .CountAsync(ct);

        var failedToday = await _db.Builds
            .Where(b => projectIds.Contains(b.ProjectId))
            .Where(b => b.QueuedAt >= today)
            .Where(b => b.Status == BuildStatus.Failed || b.Status == BuildStatus.TimedOut)
            .CountAsync(ct);

        await SendAsync(new GetDashboardResponse(
            new DashboardDto(
                recentBuilds,
                totalProjects,
                buildsToday,
                failedToday
            )
        ), cancellation: ct);
    }
}
