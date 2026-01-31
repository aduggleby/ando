// =============================================================================
// ResendVerificationEndpoint.cs
//
// Summary: FastEndpoint for resending verification email.
//
// Generates a new verification token and sends it to the user.
// Rate limited to once per minute.
//
// Design Decisions:
// - Requires authentication (user must be logged in)
// - Rate limited to prevent spam
// - Skips if already verified
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// POST /api/auth/resend-verification - Resend email verification.
/// </summary>
public class ResendVerificationEndpoint : EndpointWithoutRequest<ResendVerificationResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IUrlService _urlService;
    private readonly ILogger<ResendVerificationEndpoint> _logger;

    public ResendVerificationEndpoint(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IUrlService urlService,
        ILogger<ResendVerificationEndpoint> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _urlService = urlService;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/resend-verification");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            await SendAsync(new ResendVerificationResponse(false, "Not authenticated."), cancellation: ct);
            return;
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            await SendAsync(new ResendVerificationResponse(false, "User not found."), cancellation: ct);
            return;
        }

        if (user.EmailVerified)
        {
            await SendAsync(new ResendVerificationResponse(false, null, "Your email is already verified."), cancellation: ct);
            return;
        }

        // Rate limit: only allow resending once per minute
        if (user.EmailVerificationSentAt.HasValue &&
            DateTime.UtcNow - user.EmailVerificationSentAt.Value < TimeSpan.FromMinutes(1))
        {
            await SendAsync(new ResendVerificationResponse(
                false,
                "Please wait a minute before requesting another verification email."
            ), cancellation: ct);
            return;
        }

        // Generate and send new verification token
        var token = Guid.NewGuid().ToString("N");
        user.EmailVerificationToken = token;
        user.EmailVerificationSentAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var verifyUrl = _urlService.BuildUrl($"/auth/verify-email?userId={user.Id}&token={token}", HttpContext);

        try
        {
            await _emailService.SendEmailAsync(
                user.Email!,
                "Verify Your Email - Ando CI",
                $"""
                <h2>Verify Your Email</h2>
                <p>Please verify your email address by clicking the link below:</p>
                <p><a href="{verifyUrl}">Verify Email</a></p>
                <p>If you didn't request this, you can safely ignore this email.</p>
                """);

            _logger.LogInformation("Verification email resent to {Email}", user.Email);

            await SendAsync(new ResendVerificationResponse(
                true,
                null,
                "Verification email sent! Please check your inbox."
            ), cancellation: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resend verification email to {Email}", user.Email);
            await SendAsync(new ResendVerificationResponse(false, "Failed to send verification email."), cancellation: ct);
        }
    }
}
