// =============================================================================
// DevLoginEndpoint.cs
//
// Summary: Development-only shortcut endpoint for signing in as the seeded dev user.
//
// This endpoint exists strictly for local development workflows. It is disabled
// outside the Development environment and returns 404 when invoked elsewhere.
//
// Design Decisions:
// - Keep behavior explicit and environment-gated server-side
// - Reuse normal Identity sign-in flow to preserve cookie/session behavior
// - Return same LoginResponse shape used by standard login endpoint
// =============================================================================

using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// POST /api/auth/dev-login - Sign in as seeded development user.
/// </summary>
public class DevLoginEndpoint : EndpointWithoutRequest<LoginResponse>
{
    public const string SeededDevEmail = "dev@ando.local";

    private readonly IWebHostEnvironment _environment;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;

    public DevLoginEndpoint(
        IWebHostEnvironment environment,
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager)
    {
        _environment = environment;
        _userManager = userManager;
        _signInManager = signInManager;
    }

    public override void Configure()
    {
        Post("/auth/dev-login");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!_environment.IsDevelopment())
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var user = await _userManager.FindByEmailAsync(SeededDevEmail);
        if (user == null)
        {
            await SendAsync(new LoginResponse(false, "Development user is not seeded."), cancellation: ct);
            return;
        }

        await _signInManager.SignInAsync(user, isPersistent: true);

        user.LastLoginAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

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
    }
}
