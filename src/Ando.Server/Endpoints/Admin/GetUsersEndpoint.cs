// =============================================================================
// GetUsersEndpoint.cs
//
// Summary: FastEndpoint for listing all users (admin only).
//
// Returns paginated list of users with search and role filtering.
//
// Design Decisions:
// - Requires Admin role
// - Supports pagination, search, and role filtering
// - Includes project counts and login info
// =============================================================================

using Ando.Server.Contracts.Admin;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// GET /api/admin/users - List all users.
/// </summary>
public class GetUsersEndpoint : EndpointWithoutRequest<GetUsersResponse>
{
    private const int PageSize = 20;
    private readonly UserManager<ApplicationUser> _userManager;

    public GetUsersEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public override void Configure()
    {
        Get("/admin/users");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var search = Query<string>("search", isRequired: false);
        var role = Query<string>("role", isRequired: false);
        var page = Query<int>("page", isRequired: false);
        if (page < 1) page = 1;

        var query = _userManager.Users.AsQueryable();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(search))
        {
            var searchLower = search.ToLower();
            query = query.Where(u =>
                (u.Email != null && u.Email.ToLower().Contains(searchLower)) ||
                (u.DisplayName != null && u.DisplayName.ToLower().Contains(searchLower)));
        }

        var totalUsers = await query.CountAsync(ct);
        var totalPages = (int)Math.Ceiling(totalUsers / (double)PageSize);
        page = Math.Max(1, Math.Min(page, Math.Max(1, totalPages)));

        var users = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(u => new
            {
                u.Id,
                Email = u.Email ?? "",
                DisplayName = u.DisplayName ?? u.Email ?? "User",
                u.EmailVerified,
                u.LockoutEnd,
                u.CreatedAt,
                u.LastLoginAt,
                HasGitHubConnection = u.GitHubId != null,
                ProjectCount = u.Projects.Count
            })
            .ToListAsync(ct);

        // Check roles for each user
        var userItems = new List<UserListItemDto>();
        foreach (var u in users)
        {
            var user = await _userManager.FindByIdAsync(u.Id.ToString());
            var isAdmin = user != null && await _userManager.IsInRoleAsync(user, UserRoles.Admin);

            // Apply role filter
            if (!string.IsNullOrWhiteSpace(role))
            {
                if (role == UserRoles.Admin && !isAdmin) continue;
                if (role == UserRoles.User && isAdmin) continue;
            }

            userItems.Add(new UserListItemDto(
                u.Id,
                u.Email,
                u.DisplayName,
                u.EmailVerified,
                isAdmin,
                u.LockoutEnd.HasValue && u.LockoutEnd > DateTimeOffset.UtcNow,
                u.CreatedAt,
                u.LastLoginAt,
                u.HasGitHubConnection,
                u.ProjectCount
            ));
        }

        await SendAsync(new GetUsersResponse(
            userItems,
            page,
            totalPages,
            totalUsers,
            PageSize,
            search,
            role
        ), cancellation: ct);
    }
}
