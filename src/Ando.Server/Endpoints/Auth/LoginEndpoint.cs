// =============================================================================
// LoginEndpoint.cs
//
// Summary: FastEndpoint for user login with email and password.
//
// Authenticates users against ASP.NET Core Identity and creates a cookie-based
// session. Returns user information on successful login.
//
// Design Decisions:
// - Uses Identity's SignInManager for consistent authentication
// - Updates last login timestamp on successful login
// - Returns user details for frontend state initialization
// =============================================================================

using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// POST /api/auth/login - Authenticate user with email and password.
/// </summary>
public class LoginEndpoint : Endpoint<LoginRequest, LoginResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ILogger<LoginEndpoint> _logger;

    public LoginEndpoint(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ILogger<LoginEndpoint> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(LoginRequest req, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null)
        {
            await SendAsync(new LoginResponse(false, "Invalid email or password."), cancellation: ct);
            return;
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            req.Password,
            req.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // Update last login time
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            _logger.LogInformation(
                "User {Email} logged in via API. RememberMe={RememberMe} remoteIp={RemoteIp} xff={Xff} xfproto={XfProto}",
                req.Email,
                req.RememberMe,
                HttpContext.Connection.RemoteIpAddress?.ToString() ?? "(unknown)",
                HttpContext.Request.Headers["X-Forwarded-For"].ToString(),
                HttpContext.Request.Headers["X-Forwarded-Proto"].ToString());

            var isAdmin = await _userManager.IsInRoleAsync(user, UserRoles.Admin);

            await SendAsync(new LoginResponse(
                true,
                User: new UserDto(
                    user.Id,
                    user.Email!,
                    user.DisplayName,
                    user.EmailVerified,
                    isAdmin,
                    user.AvatarUrl
                )
            ), cancellation: ct);
            return;
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("User {Email} account locked out", req.Email);
            await SendAsync(new LoginResponse(
                false,
                "Your account has been locked due to too many failed attempts. Please try again later."
            ), cancellation: ct);
            return;
        }

        await SendAsync(new LoginResponse(false, "Invalid email or password."), cancellation: ct);
    }
}
