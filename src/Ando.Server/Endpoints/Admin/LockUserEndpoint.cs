// =============================================================================
// LockUserEndpoint.cs
//
// Summary: FastEndpoint for locking a user account (admin only).
//
// Locks out a user for a specified number of days.
//
// Design Decisions:
// - Requires Admin role
// - Cannot lock own account
// - Audit logged
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Admin;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// POST /api/admin/users/{id}/lock - Lock user account.
/// </summary>
public class LockUserEndpoint : Endpoint<LockUserRequest, LockUserResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<LockUserEndpoint> _logger;

    public LockUserEndpoint(
        UserManager<ApplicationUser> userManager,
        ILogger<LockUserEndpoint> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/admin/users/{id}/lock");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(LockUserRequest req, CancellationToken ct)
    {
        var userId = Route<int>("id");
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        if (userId == currentUserId)
        {
            await SendAsync(new LockUserResponse(false, "You cannot lock your own account."), cancellation: ct);
            return;
        }

        var user = await _userManager.FindByIdAsync(userId.ToString());
        if (user == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (!req.LockDays.HasValue || req.LockDays < 1)
        {
            await SendAsync(new LockUserResponse(false, "Please specify lock duration."), cancellation: ct);
            return;
        }

        var lockoutEnd = DateTimeOffset.UtcNow.AddDays(req.LockDays.Value);
        await _userManager.SetLockoutEndDateAsync(user, lockoutEnd);
        _logger.LogInformation("User {UserId} locked for {Days} days by admin {AdminId}", userId, req.LockDays, currentUserId);

        await SendAsync(new LockUserResponse(true), cancellation: ct);
    }
}
