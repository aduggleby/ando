// =============================================================================
// ForgotPasswordEndpoint.cs
//
// Summary: FastEndpoint for initiating password reset.
//
// Sends a password reset email if the user exists. Always returns success
// to prevent email enumeration attacks.
//
// Design Decisions:
// - Never reveals whether email exists in database
// - Uses Identity's password reset token for security
// =============================================================================

using Ando.Server.Contracts.Auth;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// POST /api/auth/forgot-password - Request password reset email.
/// </summary>
public class ForgotPasswordEndpoint : Endpoint<ForgotPasswordRequest, ForgotPasswordResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IEmailService _emailService;
    private readonly IUrlService _urlService;
    private readonly ILogger<ForgotPasswordEndpoint> _logger;

    public ForgotPasswordEndpoint(
        UserManager<ApplicationUser> userManager,
        IEmailService emailService,
        IUrlService urlService,
        ILogger<ForgotPasswordEndpoint> logger)
    {
        _userManager = userManager;
        _emailService = emailService;
        _urlService = urlService;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/forgot-password");
        AllowAnonymous();
    }

    public override async Task HandleAsync(ForgotPasswordRequest req, CancellationToken ct)
    {
        var user = await _userManager.FindByEmailAsync(req.Email);
        if (user != null)
        {
            // Generate password reset token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);

            // Build reset URL (frontend will handle this route)
            var resetUrl = _urlService.BuildUrl(
                $"/auth/reset-password?email={Uri.EscapeDataString(req.Email)}&token={Uri.EscapeDataString(token)}",
                HttpContext);

            // Send password reset email
            try
            {
                await _emailService.SendEmailAsync(
                    req.Email,
                    "Reset Your Password - Ando CI",
                    $"""
                    <h2>Reset Your Password</h2>
                    <p>Click the link below to reset your password:</p>
                    <p><a href="{resetUrl}">Reset Password</a></p>
                    <p>If you didn't request this, you can safely ignore this email.</p>
                    <p>This link will expire in 24 hours.</p>
                    """);

                _logger.LogInformation("Password reset email sent to {Email}", req.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", req.Email);
            }
        }

        // Always return success to prevent email enumeration
        await SendAsync(new ForgotPasswordResponse(true), cancellation: ct);
    }
}
