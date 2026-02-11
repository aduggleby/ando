// =============================================================================
// AuthController.cs
//
// Summary: Handles user authentication with email/password login.
//
// This controller manages user registration, login, logout, password reset,
// and email verification. It uses ASP.NET Core Identity for security.
//
// Design Decisions:
// - First registered user automatically becomes global admin
// - Soft email verification (users can log in but see reminder banner)
// - Password reset via email token
// - Uses Identity's UserManager and SignInManager
// =============================================================================

using System.Security.Claims;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Controllers;

/// <summary>
/// Controller for email/password authentication.
/// </summary>
public class AuthController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly AndoDbContext _db;
    private readonly IEmailService _emailService;
    private readonly IUrlService _urlService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        AndoDbContext db,
        IEmailService emailService,
        IUrlService urlService,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _db = db;
        _emailService = emailService;
        _urlService = urlService;
        _logger = logger;
    }

    // -------------------------------------------------------------------------
    // Login
    // -------------------------------------------------------------------------

    /// <summary>
    /// Displays the login page.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("auth/login")]
    public IActionResult Login(string? returnUrl = null, string? error = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return SafeRedirect(returnUrl);
        }

        // If a previous POST redirected back here, keep the entered email to reduce re-typing.
        // TempData survives one redirect.
        var savedEmail = TempData["LoginEmail"] as string;

        var errorMessage = error switch
        {
            "invalid_credentials" => "Invalid email or password.",
            "account_locked" => "Your account has been locked. Please try again later.",
            "email_not_confirmed" => "Please verify your email before logging in.",
            _ => null
        };

        var model = new LoginViewModel
        {
            ReturnUrl = returnUrl,
            ErrorMessage = errorMessage,
            Email = savedEmail ?? ""
        };

        return View(model);
    }

    /// <summary>
    /// Processes the login form submission.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("auth/login")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            TempData["LoginEmail"] = model.Email;
            return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl, error = "invalid_credentials" });
        }

        var result = await _signInManager.PasswordSignInAsync(
            user,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            // Update last login time
            user.LastLoginAt = DateTime.UtcNow;
            await _userManager.UpdateAsync(user);

            _logger.LogInformation("User {Email} logged in", model.Email);
            TempData["GlobalSuccess"] = "Login successful.";
            return SafeRedirect(model.ReturnUrl);
        }

        if (result.IsLockedOut)
        {
            _logger.LogWarning("User {Email} account locked out", model.Email);
            TempData["LoginEmail"] = model.Email;
            return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl, error = "account_locked" });
        }

        TempData["LoginEmail"] = model.Email;
        return RedirectToAction(nameof(Login), new { returnUrl = model.ReturnUrl, error = "invalid_credentials" });
    }

    // -------------------------------------------------------------------------
    // Registration
    // -------------------------------------------------------------------------

    /// <summary>
    /// Displays the registration page.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("auth/register")]
    public IActionResult Register()
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectToAction("Index", "Home");
        }

        return View(new RegisterViewModel());
    }

    /// <summary>
    /// Processes the registration form submission.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("auth/register")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        // Check if this is the first user (will become admin)
        var isFirstUser = !await _userManager.Users.AnyAsync();

        var user = new ApplicationUser
        {
            UserName = model.Email,
            Email = model.Email,
            DisplayName = model.DisplayName,
            EmailVerified = false,
            CreatedAt = DateTime.UtcNow
        };

        var result = await _userManager.CreateAsync(user, model.Password);

        if (result.Succeeded)
        {
            // Assign role based on whether this is the first user
            var role = isFirstUser ? UserRoles.Admin : UserRoles.User;
            await _userManager.AddToRoleAsync(user, role);

            _logger.LogInformation(
                "New user {Email} registered as {Role}",
                model.Email,
                role);

            // Generate and send email verification token
            await SendVerificationEmailAsync(user);

            // Sign in the user immediately
            // No "remember me" choice on registration; default to a persistent session.
            await _signInManager.SignInAsync(user, isPersistent: true);

            // Redirect straight to dashboard — the email verification banner
            // on the layout already reminds users to verify their email
            return Redirect("/");
        }

        // Handle registration errors
        foreach (var error in result.Errors)
        {
            if (error.Code == "DuplicateEmail" || error.Code == "DuplicateUserName")
            {
                model.ErrorMessage = "An account with this email already exists.";
                break;
            }
            model.ErrorMessage = error.Description;
        }

        return View(model);
    }

    /// <summary>
    /// Displays the registration success page.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("auth/register-success")]
    public IActionResult RegisterSuccess(string email, bool isFirst = false)
    {
        return View(new RegisterSuccessViewModel
        {
            Email = email,
            IsFirstUser = isFirst
        });
    }

    // -------------------------------------------------------------------------
    // Logout
    // -------------------------------------------------------------------------

    /// <summary>
    /// Signs out the current user.
    /// </summary>
    [Authorize]
    [HttpGet("auth/logout")]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        _logger.LogInformation("User logged out");
        return RedirectToAction("Login");
    }

    // -------------------------------------------------------------------------
    // Forgot Password
    // -------------------------------------------------------------------------

    /// <summary>
    /// Displays the forgot password page.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("auth/forgot-password")]
    public IActionResult ForgotPassword(bool sent = false)
    {
        return View(new ForgotPasswordViewModel
        {
            EmailSent = sent
        });
    }

    /// <summary>
    /// Processes the forgot password form submission.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("auth/forgot-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user != null)
        {
            // Generate password reset token
            var token = await _userManager.GeneratePasswordResetTokenAsync(user);
            var resetPath = Url.Action(
                "ResetPassword",
                "Auth",
                new { email = model.Email, token });
            var resetUrl = _urlService.BuildUrl(
                resetPath ?? "/auth/reset-password",
                HttpContext);

            // Send password reset email
            try
            {
                await _emailService.SendEmailAsync(
                    model.Email,
                    "Reset Your Password - Ando CI",
                    $"""
                    <h2>Reset Your Password</h2>
                    <p>Click the link below to reset your password:</p>
                    <p><a href="{resetUrl}">Reset Password</a></p>
                    <p>If you didn't request this, you can safely ignore this email.</p>
                    <p>This link will expire in 24 hours.</p>
                    """);

                _logger.LogInformation("Password reset email sent to {Email}", model.Email);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send password reset email to {Email}", model.Email);
            }
        }

        // Always show success to prevent email enumeration.
        // PRG: redirect to GET so refresh doesn't re-submit the POST.
        return RedirectToAction(nameof(ForgotPassword), new { sent = true });
    }

    // -------------------------------------------------------------------------
    // Reset Password
    // -------------------------------------------------------------------------

    /// <summary>
    /// Displays the reset password page.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("auth/reset-password")]
    public IActionResult ResetPassword(string email, string token)
    {
        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
        {
            return RedirectToAction("Login", new { error = "invalid_reset_link" });
        }

        var model = new ResetPasswordViewModel
        {
            Email = email,
            Token = token,
            ErrorMessage = TempData["ResetPasswordError"] as string
        };

        return View(model);
    }

    /// <summary>
    /// Processes the reset password form submission.
    /// </summary>
    [AllowAnonymous]
    [HttpPost("auth/reset-password")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            // Don't reveal that the user doesn't exist
            return RedirectToAction("Login");
        }

        var result = await _userManager.ResetPasswordAsync(user, model.Token, model.Password);
        if (result.Succeeded)
        {
            _logger.LogInformation("Password reset successful for {Email}", model.Email);
            TempData["Success"] = "Your password has been reset. You can now log in with your new password.";
            return RedirectToAction("Login");
        }

        // PRG: redirect back to GET so refresh doesn't re-submit the POST.
        TempData["ResetPasswordError"] = result.Errors.FirstOrDefault()?.Description ?? "Failed to reset password.";
        return RedirectToAction(nameof(ResetPassword), new { email = model.Email, token = model.Token });
    }

    // -------------------------------------------------------------------------
    // Email Verification
    // -------------------------------------------------------------------------

    /// <summary>
    /// Verifies the user's email address.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("auth/verify-email")]
    public async Task<IActionResult> VerifyEmail(string userId, string token)
    {
        var model = new VerifyEmailViewModel();

        if (string.IsNullOrEmpty(userId) || string.IsNullOrEmpty(token))
        {
            model.Success = false;
            model.Message = "Invalid verification link.";
            return View(model);
        }

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null)
        {
            model.Success = false;
            model.Message = "User not found.";
            return View(model);
        }

        // Check our custom email verification token
        if (user.EmailVerificationToken != token)
        {
            model.Success = false;
            model.Message = "Invalid or expired verification link.";
            return View(model);
        }

        // Mark email as verified
        user.EmailVerified = true;
        user.EmailConfirmed = true;
        user.EmailVerificationToken = null;
        await _userManager.UpdateAsync(user);

        _logger.LogInformation("Email verified for {Email}", user.Email);

        model.Success = true;
        model.Message = "Your email has been verified successfully!";
        return View(model);
    }

    /// <summary>
    /// Resends the verification email.
    /// </summary>
    [Authorize]
    [HttpPost("auth/resend-verification")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendVerification()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user == null)
        {
            return RedirectToAction("Login");
        }

        if (user.EmailVerified)
        {
            TempData["Info"] = "Your email is already verified.";
            return RedirectToAction("Index", "Home");
        }

        // Rate limit: only allow resending once per minute
        if (user.EmailVerificationSentAt.HasValue &&
            DateTime.UtcNow - user.EmailVerificationSentAt.Value < TimeSpan.FromMinutes(1))
        {
            TempData["Error"] = "Please wait a minute before requesting another verification email.";
            return RedirectToAction("Index", "Home");
        }

        await SendVerificationEmailAsync(user);

        TempData["Success"] = "Verification email sent! Please check your inbox.";
        return RedirectToAction("Index", "Home");
    }

    // -------------------------------------------------------------------------
    // Access Denied
    // -------------------------------------------------------------------------

    /// <summary>
    /// Displays the access denied page.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("auth/access-denied")]
    public IActionResult AccessDenied()
    {
        return View();
    }

    // -------------------------------------------------------------------------
    // Helper Methods
    // -------------------------------------------------------------------------

    /// <summary>
    /// Safely redirects to a URL, preventing open redirect attacks (OWASP A01:2021).
    /// Only allows relative URLs within the application.
    /// </summary>
    private IActionResult SafeRedirect(string? returnUrl)
    {
        var destination = "/";

        // Only allow local (relative) URLs to prevent open redirect attacks
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            destination = returnUrl;
        }

        // Use 303 See Other so form POSTs always complete as a GET on the destination.
        Response.Headers.Location = destination;
        return StatusCode(StatusCodes.Status303SeeOther);
    }

    /// <summary>
    /// Generates and sends a verification email to the user.
    /// </summary>
    private async Task SendVerificationEmailAsync(ApplicationUser user)
    {
        // Generate a simple verification token
        var token = Guid.NewGuid().ToString("N");
        user.EmailVerificationToken = token;
        user.EmailVerificationSentAt = DateTime.UtcNow;
        await _userManager.UpdateAsync(user);

        var verifyUrl = Url.Action(
            "VerifyEmail",
            "Auth",
            new { userId = user.Id.ToString(), token });
        var verifyLink = _urlService.BuildUrl(
            verifyUrl ?? "/auth/verify-email",
            HttpContext);

        try
        {
            await _emailService.SendEmailAsync(
                user.Email!,
                "Verify Your Email - Ando CI",
                $"""
                <h2>Welcome to Ando CI!</h2>
                <p>Please verify your email address by clicking the link below:</p>
                <p><a href="{verifyLink}">Verify Email</a></p>
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
