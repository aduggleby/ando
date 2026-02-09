// =============================================================================
// RegisterEndpoint.cs
//
// Summary: FastEndpoint for user registration.
//
// Creates a new user account with email and password. The first registered
// user automatically becomes an admin. Sends email verification after
// registration.
//
// Design Decisions:
// - First user becomes admin automatically
// - Signs in user immediately after registration
// - Sends verification email asynchronously
// =============================================================================

using Ando.Server.Contracts.Auth;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Auth;

/// <summary>
/// POST /api/auth/register - Register a new user account.
/// </summary>
public class RegisterEndpoint : Endpoint<RegisterRequest, RegisterResponse>
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IEmailService _emailService;
    private readonly IUrlService _urlService;
    private readonly ILogger<RegisterEndpoint> _logger;

    public RegisterEndpoint(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IEmailService emailService,
        IUrlService urlService,
        ILogger<RegisterEndpoint> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _emailService = emailService;
        _urlService = urlService;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/auth/register");
        AllowAnonymous();
    }

    public override async Task HandleAsync(RegisterRequest req, CancellationToken ct)
    {
        // Check if this is the first user (will become admin)
        var isFirstUser = !await _userManager.Users.AnyAsync(ct);

        var user = new ApplicationUser
        {
            UserName = req.Email,
            Email = req.Email,
            DisplayName = req.DisplayName,
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, req.Password);

        if (result.Succeeded)
        {
            // Assign role based on whether this is the first user
            var role = isFirstUser ? UserRoles.Admin : UserRoles.User;
            await _userManager.AddToRoleAsync(user, role);

            _logger.LogInformation(
                "New user {Email} registered as {Role} via API",
                req.Email,
                role);

            // Generate and send email verification token
            await SendVerificationEmailAsync(user);

            // Sign in the user immediately
            // No "remember me" choice on registration; default to a persistent session.
            await _signInManager.SignInAsync(user, isPersistent: true);

            await SendAsync(new RegisterResponse(
                true,
                IsFirstUser: isFirstUser,
                User: new UserDto(
                    user.Id,
                    user.Email!,
                    user.DisplayName,
                    user.EmailVerified,
                    isFirstUser,
                    user.AvatarUrl
                )
            ), cancellation: ct);
            return;
        }

        // Handle registration errors
        var errorMessage = "Registration failed.";
        foreach (var error in result.Errors)
        {
            if (error.Code == "DuplicateEmail" || error.Code == "DuplicateUserName")
            {
                errorMessage = "An account with this email already exists.";
                break;
            }
            errorMessage = error.Description;
        }

        await SendAsync(new RegisterResponse(false, errorMessage), cancellation: ct);
    }

    private async Task SendVerificationEmailAsync(ApplicationUser user)
    {
        // Generate a simple verification token
        var token = Guid.NewGuid().ToString("N");
        user.EmailVerificationToken = token;
        user.EmailVerificationSentAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        // Build verification URL (frontend will handle this route)
        var verifyUrl = _urlService.BuildUrl($"/auth/verify-email?userId={user.Id}&token={token}", HttpContext);

        try
        {
            await _emailService.SendEmailAsync(
                user.Email!,
                "Verify Your Email - Ando CI",
                $"""
                <h2>Welcome to Ando CI!</h2>
                <p>Please verify your email address by clicking the link below:</p>
                <p><a href="{verifyUrl}">Verify Email</a></p>
                <p>If you didn't create an account, you can safely ignore this email.</p>
                """);

            _logger.LogInformation("Verification email sent to {Email}", user.Email);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send verification email to {Email}", user.Email);
        }
    }
}
