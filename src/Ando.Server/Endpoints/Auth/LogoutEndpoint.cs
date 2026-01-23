// =============================================================================
// LogoutEndpoint.cs
//
// Summary: FastEndpoint for user logout.
//
// Signs out the current user by clearing their authentication cookie.
//
// Design Decisions:
// - Requires authentication (must be logged in to log out)
// - Uses Identity's SignInManager for consistent session handling
// =============================================================================

using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// POST /api/auth/logout - Sign out the current user.
/// </summary>
public class LogoutEndpoint : EndpointWithoutRequest<LogoutResponse>
{
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LogoutEndpoint> _logger;

    public LogoutEndpoint(
        SignInManager<ApplicationUser> signInManager,
        ILogger<LogoutEndpoint> logger)
    {
        _signInManager = signInManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/logout");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out via API");
        await SendAsync(new LogoutResponse(true), cancellation: ct);
    }
}
