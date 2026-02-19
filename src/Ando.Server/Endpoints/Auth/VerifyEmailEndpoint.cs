// =============================================================================
// VerifyEmailEndpoint.cs
//
// Summary: FastEndpoint for verifying email address.
//
// Validates the verification token and marks the user's email as verified.
//
// Design Decisions:
// - Uses custom verification token (not Identity's email confirmation)
// - Clears token after successful verification
// =============================================================================

using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// POST /api/auth/verify-email - Verify email address with token.
/// </summary>
[EnableRateLimiting("auth-verification")]
public class VerifyEmailEndpoint : Endpoint<VerifyEmailRequest, VerifyEmailResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILogger<VerifyEmailEndpoint> _logger;

    public VerifyEmailEndpoint(
        UserManager<ApplicationUser> userManager,
        ILogger<VerifyEmailEndpoint> logger)
    {
        _userManager = userManager;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/verify-email");
        AllowAnonymous();
    }

    public override async Task HandleAsync(VerifyEmailRequest req, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(req.UserId) || string.IsNullOrEmpty(req.Token))
        {
            await SendAsync(new VerifyEmailResponse(false, "Invalid verification link."), cancellation: ct);
            return;
        }

        var user = await _userManager.FindByIdAsync(req.UserId);
        if (user == null)
        {
            await SendAsync(new VerifyEmailResponse(false, "User not found."), cancellation: ct);
            return;
        }

        // Check our custom email verification token
        if (user.EmailVerificationToken != req.Token)
        {
            await SendAsync(new VerifyEmailResponse(false, "Invalid or expired verification link."), cancellation: ct);
            return;
        }

        // Mark email as verified
        user.EmailVerified = true;
        user.EmailConfirmed = true;
        user.EmailVerificationToken = null;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Email verified for {Email}", user.Email);

        await SendAsync(new VerifyEmailResponse(true, "Your email has been verified successfully!"), cancellation: ct);
    }
}
