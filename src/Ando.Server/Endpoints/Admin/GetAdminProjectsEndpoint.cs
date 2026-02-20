// =============================================================================
// GetAdminProjectsEndpoint.cs
//
// Summary: FastEndpoint for listing all projects (admin only).
//
// Returns paginated list of all projects in the system with owner info.
//
// Design Decisions:
// - Requires Admin role
// - Shows all projects across all users
// - Supports search and pagination
// =============================================================================

using Ando.Server.Contracts.Admin;
using Ando.Server.Data;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// GET /api/admin/projects - List all projects.
/// </summary>
public class GetAdminProjectsEndpoint : EndpointWithoutRequest<GetAdminProjectsResponse>
{
    private const int PageSize = 20;
    private readonly AndoDbContext _db;

    public GetAdminProjectsEndpoint(AndoDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/admin/projects");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var search = Query<string>("search", isRequired: false);
        var page = Query<int>("page", isRequired: false);
        if (page < 1) page = 1;

        var query = _db.Projects
            .Include(p => p.Owner)
            .Include(p => p.Builds)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(p =>
                p.RepoFullName.ToLower().Contains(searchLower) ||
                (p.Owner.Email != null && p.Owner.Email.ToLower().Contains(searchLower)));
        }

        var totalProjects = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalProjects / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var projects = await query
            .OrderByDescending(p => p.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(p => new
            {
                p.Id,
                p.RepoFullName,
                OwnerEmail = p.Owner.Email ?? "",
                OwnerDisplayName = p.Owner.DisplayName ?? p.Owner.Email ?? "User",
                OwnerId = p.Owner.Id,
                p.CreatedAt,
                BuildCount = p.Builds.Count,
                LastBuild = p.Builds
                    .OrderByDescending(b => b.QueuedAt)
                    .Select(b => new { b.QueuedAt, b.Status })
                    .FirstOrDefault()
            })
            .ToListAsync(ct);

        var projectDtos = projects
            .Select(p => new AdminProjectDto(
                p.Id,
                p.RepoFullName,
                null,
                p.OwnerEmail,
                p.OwnerDisplayName,
                p.OwnerId,
                p.CreatedAt,
                p.BuildCount,
                p.LastBuild?.QueuedAt,
                p.LastBuild?.Status.ToString()
            ))
            .ToList();

        await SendAsync(new GetAdminProjectsResponse(
            projectDtos,
            page,
            totalPages,
            totalProjects,
            PageSize,
            search
        ), cancellation: ct);
    }
}
