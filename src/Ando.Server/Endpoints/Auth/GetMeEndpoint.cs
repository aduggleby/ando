// =============================================================================
// GetMeEndpoint.cs
//
// Summary: FastEndpoint to get the current authenticated user's information.
//
// Returns the currently logged-in user's profile, or indicates not authenticated.
// Used by the React frontend to initialize auth state on page load.
//
// Design Decisions:
// - Returns isAuthenticated: false rather than 401 when not logged in
// - Allows anonymous access so frontend can check auth state
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// GET /api/auth/me - Get current authenticated user.
/// </summary>
public class GetMeEndpoint : EndpointWithoutRequest<GetMeResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<GetMeEndpoint> _logger;

    public GetMeEndpoint(
        UserManager<ApplicationUser> userManager,
        ILogger<GetMeEndpoint> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/auth/me");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            var hasCookie = HttpContext.Request.Headers.Cookie.Count > 0;
            var cookieLength = HttpContext.Request.Headers.Cookie.ToString().Length;
            _logger.LogDebug(
                "GetMe: not authenticated. hasCookieHeader={HasCookie} cookieHeaderLength={CookieLength} "
                + "remoteIp={RemoteIp} xff={Xff} xfproto={XfProto}",
                hasCookie,
                cookieLength,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                HttpContext.Request.Headers["X-Forwarded-Proto"].ToString());

            await SendAsync(new GetMeResponse(false), cancellation: ct);
            return;
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            _logger.LogWarning("GetMe: authenticated but no NameIdentifier claim. User={User}",
                User.Identity?.Name ?? "(unknown)");
            await SendAsync(new GetMeResponse(false), cancellation: ct);
            return;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            _logger.LogWarning("GetMe: authenticated but user not found in DB. UserId={UserId} User={User}",
                userId, User.Identity?.Name ?? "(unknown)");
            await SendAsync(new GetMeResponse(false), cancellation: ct);
            return;
        }

        var isAdmin = await _userManager.IsInRoleAsync(user, UserRoles.Admin);

        await SendAsync(new GetMeResponse(
            true,
            new UserDto(
                user.Id,
                user.Email!,
                user.DisplayName,
                user.EmailVerified,
                isAdmin,
                user.AvatarUrl
            )
        ), cancellation: ct);
    }
}
