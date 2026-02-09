// =============================================================================
// AuthViewModels.cs
//
// Summary: View models for authentication-related views.
//
// These models are used for the login, registration, password reset, and
// email verification flows. They include data annotations for validation.
//
// Design Decisions:
// - Records for immutable view models where appropriate
// - Data annotations for client and server-side validation
// - Separate models for request (form submission) and display (page rendering)
// =============================================================================

using System.ComponentModel.DataAnnotations;

namespace Ando.Server.ViewModels;

/// <summary>
/// View model for the login page.
/// </summary>
public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Display(Name = "Remember me")]
    public bool RememberMe { get; set; } = true;

    /// <summary>
    /// URL to redirect to after successful login.
    /// </summary>
    public string? ReturnUrl { get; set; }

    /// <summary>
    /// Error message to display (e.g., invalid credentials).
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// View model for the registration page.
/// </summary>
public class RegisterViewModel
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    [Display(Name = "Display Name")]
    [StringLength(100, ErrorMessage = "Display name cannot exceed 100 characters")]
    public string? DisplayName { get; set; }

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    [DataType(DataType.Password)]
    [Display(Name = "Password")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>
    /// Error message to display (e.g., email already exists).
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// View model for the forgot password page.
/// </summary>
public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    [Display(Name = "Email")]
    public string Email { get; set; } = "";

    /// <summary>
    /// Whether the password reset email was sent successfully.
    /// </summary>
    public bool EmailSent { get; set; }

    /// <summary>
    /// Error message to display.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// View model for the reset password page.
/// </summary>
public class ResetPasswordViewModel
{
    [Required]
    public string Email { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password")]
    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = "";

    /// <summary>
    /// Error message to display.
    /// </summary>
    public string? ErrorMessage { get; set; }
}

/// <summary>
/// View model for the email verification result page.
/// </summary>
public class VerifyEmailViewModel
{
    /// <summary>
    /// Whether the email was verified successfully.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Message to display to the user.
    /// </summary>
    public string Message { get; set; } = "";
}

/// <summary>
/// View model for the registration success page.
/// </summary>
public class RegisterSuccessViewModel
{
    /// <summary>
    /// The email address that was registered.
    /// </summary>
    public string Email { get; set; } = "";

    /// <summary>
    /// Whether this is the first user (admin).
    /// </summary>
    public bool IsFirstUser { get; set; }
}
