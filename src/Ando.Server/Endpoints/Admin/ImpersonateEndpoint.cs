// =============================================================================
// ImpersonateEndpoint.cs
//
// Summary: FastEndpoint for starting user impersonation (admin only).
//
// Allows admins to impersonate regular users for support purposes.
//
// Design Decisions:
// - Requires Admin role
// - Cannot impersonate other admins
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
/// POST /api/admin/users/{id}/impersonate - Start impersonating a user.
/// </summary>
public class ImpersonateEndpoint : EndpointWithoutRequest<ImpersonateResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IImpersonationService _impersonationService;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<ImpersonateEndpoint> _logger;

    public ImpersonateEndpoint(
        UserManager<ApplicationUser> userManager,
        IImpersonationService impersonationService,
        IAuditLogger auditLogger,
        ILogger<ImpersonateEndpoint> logger)
    {
        _userManager = userManager;
        _impersonationService = impersonationService;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/admin/users/{id}/impersonate");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var targetUserId = Route<int>("id");
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var targetUser = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (targetUser == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        // Cannot impersonate admins
        if (await _userManager.IsInRoleAsync(targetUser, UserRoles.Admin))
        {
            await SendAsync(new ImpersonateResponse(false, "Cannot impersonate administrator accounts."), cancellation: ct);
            return;
        }

        var success = await _impersonationService.StartImpersonationAsync(currentUserId, targetUserId);

        var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value;

        if (success)
        {
            _auditLogger.LogAdminAction(
                "ImpersonationStarted",
                $"Admin started impersonating user",
                currentUserId,
                adminEmail,
                targetUserId,
                targetUser.Email);

            await SendAsync(new ImpersonateResponse(true), cancellation: ct);
        }
        else
        {
            _auditLogger.LogAdminAction(
                "ImpersonationFailed",
                "Failed to start impersonation",
                currentUserId,
                adminEmail,
                targetUserId,
                targetUser.Email,
                success: false);

            await SendAsync(new ImpersonateResponse(false, "Failed to start impersonation."), cancellation: ct);
        }
    }
}
