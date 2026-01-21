// =============================================================================
// ResetPasswordEndpoint.cs
//
// Summary: FastEndpoint for resetting password with token.
//
// Validates the reset token and updates the user's password.
//
// Design Decisions:
// - Uses Identity's password reset mechanism
// - Returns generic error for invalid tokens (security)
// =============================================================================

using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// POST /api/auth/reset-password - Reset password with token.
/// </summary>
public class ResetPasswordEndpoint : Endpoint<ResetPasswordRequest, ResetPasswordResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<ResetPasswordEndpoint> _logger;

    public ResetPasswordEndpoint(
        UserManager<ApplicationUser> userManager,
        ILogger<ResetPasswordEndpoint> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/reset-password");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ResetPasswordRequest req, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user == null)
        {
            // Don't reveal that the user doesn't exist
            await SendAsync(new ResetPasswordResponse(false, "Invalid or expired reset link."), cancellation: ct);
            return;
        }

        var result = await _userManager.ResetPasswordAsync(user, req.Token, req.Password);
        if (result.Succeeded)
        {
            _logger.LogInformation("Password reset successful for {Email}", req.Email);
            await SendAsync(new ResetPasswordResponse(true), cancellation: ct);
            return;
        }

        var errorMessage = "Invalid or expired reset link.";
        foreach (var error in result.Errors)
        {
            if (error.Code.Contains("Password"))
            {
                errorMessage = error.Description;
                break;
            }
        }

        await SendAsync(new ResetPasswordResponse(false, errorMessage), cancellation: ct);
    }
}
