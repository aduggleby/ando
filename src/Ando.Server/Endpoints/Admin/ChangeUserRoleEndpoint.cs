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
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// POST /api/admin/users/{id}/role - Change user role.
/// </summary>
public class ChangeUserRoleEndpoint : Endpoint<ChangeUserRoleRequest, ChangeUserRoleResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<ChangeUserRoleEndpoint> _logger;

    public ChangeUserRoleEndpoint(
        UserManager<ApplicationUser> userManager,
        IAuditLogger auditLogger,
        ILogger<ChangeUserRoleEndpoint> logger)
    {
        _userManager = userManager;
        _auditLogger = auditLogger;
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

        var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value;

        if (isCurrentlyAdmin && !wantsAdmin)
        {
            // Demote from admin
            await _userManager.RemoveFromRoleAsync(user, UserRoles.Admin);
            await _userManager.AddToRoleAsync(user, UserRoles.User);

            _auditLogger.LogAdminAction(
                "UserDemoted",
                $"User demoted from Admin to User",
                currentUserId,
                adminEmail,
                userId,
                user.Email,
                new Dictionary<string, object> { ["previousRole"] = "Admin", ["newRole"] = "User" });
        }
        else if (!isCurrentlyAdmin && wantsAdmin)
        {
            // Promote to admin
            await _userManager.RemoveFromRoleAsync(user, UserRoles.User);
            await _userManager.AddToRoleAsync(user, UserRoles.Admin);

            _auditLogger.LogAdminAction(
                "UserPromoted",
                $"User promoted to Admin",
                currentUserId,
                adminEmail,
                userId,
                user.Email,
                new Dictionary<string, object> { ["previousRole"] = "User", ["newRole"] = "Admin" });
        }

        await SendAsync(new ChangeUserRoleResponse(true), cancellation: ct);
    }
}
