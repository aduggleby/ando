// =============================================================================
// GetUserDetailsEndpoint.cs
//
// Summary: FastEndpoint for getting user details (admin only).
//
// Returns full user details including projects and activity.
//
// Design Decisions:
// - Requires Admin role
// - Includes all user projects and build counts
// =============================================================================

using Ando.Server.Contracts.Admin;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// GET /api/admin/users/{id} - Get user details.
/// </summary>
public class GetUserDetailsEndpoint : EndpointWithoutRequest<GetUserDetailsResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;

    public GetUserDetailsEndpoint(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public override void Configure()
    {
        Get("/admin/users/{id}");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Route<int>("id");

        var user = await _userManager.Users
            .Include(u => u.Projects)
            .ThenInclude(p => p.Builds)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, UserRoles.Admin);

        var projects = user.Projects.Select(p => new UserProjectDto(
            p.Id,
            p.RepoFullName,
            null,
            p.CreatedAt,
            p.Builds.Count
        )).ToList();

        await SendAsync(new GetUserDetailsResponse(
            new UserDetailsDto(
                user.Id,
                user.Email ?? "",
                user.DisplayName,
                user.AvatarUrl,
                user.EmailVerified,
                user.EmailVerificationSentAt,
                isAdmin,
                user.LockoutEnd.HasValue && user.LockoutEnd > DateTimeOffset.UtcNow,
                user.LockoutEnd,
                user.CreatedAt,
                user.LastLoginAt,
                user.HasGitHubConnection,
                user.GitHubLogin,
                user.GitHubConnectedAt,
                projects,
                user.Projects.Sum(p => p.Builds.Count)
            )
        ), cancellation: ct);
    }
}
