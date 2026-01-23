// =============================================================================
// GetAdminDashboardEndpoint.cs
//
// Summary: FastEndpoint for admin dashboard with system statistics.
//
// Returns system-wide statistics including user counts, project counts,
// build counts, and recent activity.
//
// Design Decisions:
// - Requires Admin role
// - Returns aggregate statistics for quick overview
// - Includes recent users and builds
// =============================================================================

using Ando.Server.Contracts.Admin;
using Ando.Server.Data;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// GET /api/admin/dashboard - Admin dashboard statistics.
/// </summary>
public class GetAdminDashboardEndpoint : EndpointWithoutRequest<AdminDashboardDto>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AndoDbContext _db;

    public GetAdminDashboardEndpoint(UserManager<ApplicationUser> userManager, AndoDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    public override void Configure()
    {
        Get("/admin/dashboard");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var yesterday = now.AddDays(-1);

        var totalUsers = await _userManager.Users.CountAsync(ct);
        var verifiedUsers = await _userManager.Users.CountAsync(u => u.EmailVerified, ct);
        var adminUsers = (await _userManager.GetUsersInRoleAsync(UserRoles.Admin)).Count;

        var totalProjects = await _db.Projects.CountAsync(ct);
        var totalBuilds = await _db.Builds.CountAsync(ct);
        var recentBuilds = await _db.Builds.CountAsync(b => b.QueuedAt >= yesterday, ct);

        var recentUsersList = await _userManager.Users
            .OrderByDescending(u => u.CreatedAt)
            .Take(5)
            .Select(u => new RecentUserDto(
                u.Id,
                u.Email ?? "",
                u.DisplayName ?? u.Email ?? "User",
                u.CreatedAt,
                u.EmailVerified
            ))
            .ToListAsync(ct);

        var recentBuildsList = await _db.Builds
            .Include(b => b.Project)
            .Where(b => b.QueuedAt >= yesterday)
            .OrderByDescending(b => b.QueuedAt)
            .Take(10)
            .Select(b => new RecentBuildDto(
                b.Id,
                b.Project.RepoFullName,
                b.Branch,
                b.Status.ToString(),
                b.QueuedAt
            ))
            .ToListAsync(ct);

        await SendAsync(new AdminDashboardDto(
            totalUsers,
            verifiedUsers,
            totalUsers - verifiedUsers,
            adminUsers,
            totalProjects,
            totalBuilds,
            recentBuilds,
            recentUsersList,
            recentBuildsList
        ), cancellation: ct);
    }
}
