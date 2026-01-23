// =============================================================================
// ImpersonationService.cs
//
// Summary: Service for admin user impersonation functionality.
//
// Allows administrators to impersonate other users for support and debugging.
// The original admin identity is stored in the session so they can stop
// impersonating at any time. Impersonation is logged for audit purposes.
//
// Design Decisions:
// - Session-based storage of original admin ID (simple, secure)
// - Separate sign-in preserves impersonation state across requests
// - Only admins can impersonate; admins cannot impersonate other admins
// =============================================================================

using Microsoft.AspNetCore.Identity;
using Ando.Server.Models;

namespace Ando.Server.Services;

/// <summary>
/// Interface for user impersonation functionality.
/// </summary>
public interface IImpersonationService
{
    /// <summary>
    /// Starts impersonating a user. Stores the original admin ID in session.
    /// </summary>
    Task<bool> StartImpersonationAsync(int adminUserId, int targetUserId);

    /// <summary>
    /// Stops impersonation and restores the original admin session.
    /// </summary>
    Task<bool> StopImpersonationAsync();

    /// <summary>
    /// Checks if the current session is an impersonation session.
    /// </summary>
    bool IsImpersonating { get; }

    /// <summary>
    /// Gets the original admin user ID if impersonating, null otherwise.
    /// </summary>
    int? OriginalAdminId { get; }

    /// <summary>
    /// Gets the impersonated user ID if impersonating, null otherwise.
    /// </summary>
    int? ImpersonatedUserId { get; }
}

/// <summary>
/// Implementation of user impersonation using session storage.
/// </summary>
public class ImpersonationService : IImpersonationService
{
    private const string OriginalAdminIdKey = "ImpersonationOriginalAdminId";
    private const string ImpersonatedUserIdKey = "ImpersonationTargetUserId";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<ImpersonationService> _logger;

    public ImpersonationService(
        IHttpContextAccessor httpContextAccessor,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<ImpersonationService> logger)
    {
        _httpContextAccessor = httpContextAccessor;
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    public bool IsImpersonating => OriginalAdminId.HasValue;

    public int? OriginalAdminId
    {
        get
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null) return null;

            var value = session.GetInt32(OriginalAdminIdKey);
            return value;
        }
    }

    public int? ImpersonatedUserId
    {
        get
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            if (session == null) return null;

            return session.GetInt32(ImpersonatedUserIdKey);
        }
    }

    public async Task<bool> StartImpersonationAsync(int adminUserId, int targetUserId)
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null)
        {
            _logger.LogWarning("Cannot start impersonation: no session available");
            return false;
        }

        // Verify admin exists and is actually an admin
        var admin = await _userManager.FindByIdAsync(adminUserId.ToString());
        if (admin == null || !await _userManager.IsInRoleAsync(admin, UserRoles.Admin))
        {
            _logger.LogWarning("Impersonation failed: user {AdminId} is not an admin", adminUserId);
            return false;
        }

        // Get target user
        var targetUser = await _userManager.FindByIdAsync(targetUserId.ToString());
        if (targetUser == null)
        {
            _logger.LogWarning("Impersonation failed: target user {TargetId} not found", targetUserId);
            return false;
        }

        // Prevent impersonating other admins
        if (await _userManager.IsInRoleAsync(targetUser, UserRoles.Admin))
        {
            _logger.LogWarning("Impersonation failed: cannot impersonate admin user {TargetId}", targetUserId);
            return false;
        }

        // Store original admin ID and target user ID in session
        session.SetInt32(OriginalAdminIdKey, adminUserId);
        session.SetInt32(ImpersonatedUserIdKey, targetUserId);

        // Sign in as the target user
        await _signInManager.SignInAsync(targetUser, isPersistent: false);

        _logger.LogInformation(
            "Admin {AdminId} ({AdminEmail}) started impersonating user {TargetId} ({TargetEmail})",
            adminUserId, admin.Email, targetUserId, targetUser.Email);

        return true;
    }

    public async Task<bool> StopImpersonationAsync()
    {
        var session = _httpContextAccessor.HttpContext?.Session;
        if (session == null)
        {
            _logger.LogWarning("Cannot stop impersonation: no session available");
            return false;
        }

        var originalAdminId = session.GetInt32(OriginalAdminIdKey);
        var impersonatedUserId = session.GetInt32(ImpersonatedUserIdKey);

        if (!originalAdminId.HasValue)
        {
            _logger.LogWarning("Cannot stop impersonation: not currently impersonating");
            return false;
        }

        // Get the original admin user
        var admin = await _userManager.FindByIdAsync(originalAdminId.Value.ToString());
        if (admin == null)
        {
            _logger.LogError("Cannot stop impersonation: original admin {AdminId} not found", originalAdminId);
            // Clear session anyway to prevent stuck state
            session.Remove(OriginalAdminIdKey);
            session.Remove(ImpersonatedUserIdKey);
            return false;
        }

        // Clear impersonation state from session
        session.Remove(OriginalAdminIdKey);
        session.Remove(ImpersonatedUserIdKey);

        // Sign back in as the admin
        await _signInManager.SignInAsync(admin, isPersistent: false);

        _logger.LogInformation(
            "Admin {AdminId} ({AdminEmail}) stopped impersonating user {TargetId}",
            originalAdminId, admin.Email, impersonatedUserId);

        return true;
    }
}
