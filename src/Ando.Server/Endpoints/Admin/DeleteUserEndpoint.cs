// =============================================================================
// DeleteUserEndpoint.cs
//
// Summary: FastEndpoint for deleting a user account (admin only).
//
// Deletes a user and all their projects, builds, and data.
// Requires email confirmation as a safety measure.
//
// Design Decisions:
// - Requires Admin role
// - Cannot delete own account
// - Requires email confirmation
// - Cascades deletion to all user data
// - Audit logged
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Admin;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// DELETE /api/admin/users/{id} - Delete user account.
/// </summary>
public class DeleteUserEndpoint : Endpoint<DeleteUserRequest, DeleteUserResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AndoDbContext _db;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<DeleteUserEndpoint> _logger;

    public DeleteUserEndpoint(
        UserManager<ApplicationUser> userManager,
        AndoDbContext db,
        IAuditLogger auditLogger,
        ILogger<DeleteUserEndpoint> logger)
    {
        _userManager = userManager;
        _db = db;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    public override void Configure()
    {
        Delete("/admin/users/{id}");
        Roles(UserRoles.Admin);
    }

    public override async Task HandleAsync(DeleteUserRequest req, CancellationToken ct)
    {
        var userId = Route<int>("id");
        var currentUserId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        if (userId == currentUserId)
        {
            await SendAsync(new DeleteUserResponse(false, "You cannot delete your own account."), cancellation: ct);
            return;
        }

        var user = await _userManager.Users
            .Include(u => u.Projects)
            .ThenInclude(p => p.Builds)
            .FirstOrDefaultAsync(u => u.Id == userId, ct);

        if (user == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        // Verify email confirmation
        if (req.ConfirmEmail != user.Email)
        {
            await SendAsync(new DeleteUserResponse(false, "Email address does not match."), cancellation: ct);
            return;
        }

        // Delete user's projects and builds
        foreach (var project in user.Projects.ToList())
        {
            _db.Builds.RemoveRange(project.Builds);
            _db.Projects.Remove(project);
        }

        await _db.SaveChangesAsync(ct);

        // Delete the user
        var userEmail = user.Email;
        var projectCount = user.Projects.Count;
        var result = await _userManager.DeleteAsync(user);

        var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value;

        if (result.Succeeded)
        {
            _auditLogger.LogAdminAction(
                "UserDeleted",
                $"User account deleted with {projectCount} projects",
                currentUserId,
                adminEmail,
                userId,
                userEmail,
                new Dictionary<string, object> { ["projectsDeleted"] = projectCount });

            await SendAsync(new DeleteUserResponse(true), cancellation: ct);
            return;
        }

        _auditLogger.LogAdminAction(
            "UserDeleteFailed",
            "Failed to delete user account",
            currentUserId,
            adminEmail,
            userId,
            userEmail,
            success: false);

        await SendAsync(new DeleteUserResponse(false, "Failed to delete user."), cancellation: ct);
    }
}
