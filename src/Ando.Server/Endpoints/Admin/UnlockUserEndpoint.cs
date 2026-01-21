// =============================================================================
// UnlockUserEndpoint.cs
//
// Summary: FastEndpoint for unlocking a user account (admin only).
//
// Removes lockout from a user account.
//
// Design Decisions:
// - Requires Admin role
// - Audit logged
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Admin;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// POST /api/admin/users/{id}/unlock - Unlock user account.
/// </summary>
public class UnlockUserEndpoint : EndpointWithoutRequest<LockUserResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<UnlockUserEndpoint> _logger;

    public UnlockUserEndpoint(
        UserManager<ApplicationUser> userManager,
        ILogger<UnlockUserEndpoint> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/admin/users/{id}/unlock");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = Route<int>("id");
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await _userManager.SetLockoutEndDateAsync(user, null);
        _logger.LogInformation("User {UserId} unlocked by admin {AdminId}", userId, currentUserId);

        await SendAsync(new LockUserResponse(true), cancellation: ct);
    }
}
