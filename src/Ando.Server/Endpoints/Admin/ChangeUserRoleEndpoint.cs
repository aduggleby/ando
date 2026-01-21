// =============================================================================
// ChangeUserRoleEndpoint.cs
//
// Summary: FastEndpoint for changing a user's role (admin only).
//
// Promotes or demotes users between Admin and User roles.
//
// Design Decisions:
// - Requires Admin role
// - Cannot change own role (enforced)
// - Audit logged
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Admin;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// POST /api/admin/users/{id}/role - Change user role.
/// </summary>
public class ChangeUserRoleEndpoint : Endpoint<ChangeUserRoleRequest, ChangeUserRoleResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ChangeUserRoleEndpoint> _logger;

    public ChangeUserRoleEndpoint(
        UserManager<ApplicationUser> userManager,
        ILogger<ChangeUserRoleEndpoint> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/admin/users/{id}/role");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(ChangeUserRoleRequest req, CancellationToken ct)
    {
        var userId = Route<int>("id");
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        if (userId == currentUserId)
        {
            await SendAsync(new ChangeUserRoleResponse(false, "You cannot change your own role."), cancellation: ct);
            return;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var isCurrentlyAdmin = await _userManager.IsInRoleAsync(user, UserRoles.Admin);
        var wantsAdmin = req.NewRole == UserRoles.Admin;

        if (isCurrentlyAdmin && !wantsAdmin)
        {
            // Demote from admin
            await _userManager.RemoveFromRoleAsync(user, UserRoles.Admin);
            await _userManager.AddToRoleAsync(user, UserRoles.User);
            _logger.LogInformation("User {UserId} demoted from Admin to User by {AdminId}", userId, currentUserId);
        }
        else if (!isCurrentlyAdmin && wantsAdmin)
        {
            // Promote to admin
            await _userManager.RemoveFromRoleAsync(user, UserRoles.User);
            await _userManager.AddToRoleAsync(user, UserRoles.Admin);
            _logger.LogInformation("User {UserId} promoted to Admin by {AdminId}", userId, currentUserId);
        }

        await SendAsync(new ChangeUserRoleResponse(true), cancellation: ct);
    }
}
