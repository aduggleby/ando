// =============================================================================
// AuthContracts.cs
//
// Summary: Request and response DTOs for authentication API endpoints.
//
// These contracts define the shape of data exchanged between the React frontend
// and FastEndpoints for authentication operations including login, register,
// logout, password reset, and email verification.
//
// Design Decisions:
// - Records for immutable response types
// - Classes for mutable request types (for model binding)
// - Validation attributes for request validation
// - Nullable reference types for optional fields
// =============================================================================

using System.ComponentModel.DataAnnotations;

namespace Ando.Server.Contracts.Auth;

// =============================================================================
// Login
// =============================================================================

/// <summary>
/// Request to log in with email and password.
/// </summary>
public class LoginRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = "";

    [Required(ErrorMessage = "Password is required")]
    public string Password { get; set; } = "";

    public bool RememberMe { get; set; }
}

/// <summary>
/// Response from login attempt.
/// </summary>
public record LoginResponse(
    bool Success,
    string? Error = null,
    UserDto? User = null
);

// =============================================================================
// Register
// =============================================================================

/// <summary>
/// Request to register a new account.
/// </summary>
public class RegisterRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = "";

    [StringLength(100, ErrorMessage = "Display name cannot exceed 100 characters")]
    public string? DisplayName { get; set; }

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// Response from registration attempt.
/// </summary>
public record RegisterResponse(
    bool Success,
    string? Error = null,
    bool IsFirstUser = false,
    UserDto? User = null
);

// =============================================================================
// Forgot Password
// =============================================================================

/// <summary>
/// Request to initiate password reset.
/// </summary>
public class ForgotPasswordRequest
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = "";
}

/// <summary>
/// Response from forgot password request.
/// Always returns success to prevent email enumeration.
/// </summary>
public record ForgotPasswordResponse(
    bool Success,
    string Message = "If an account with that email exists, a password reset link has been sent."
);

// =============================================================================
// Reset Password
// =============================================================================

/// <summary>
/// Request to reset password with token.
/// </summary>
public class ResetPasswordRequest
{
    [Required]
    public string Email { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = "";

    [Required(ErrorMessage = "Please confirm your password")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// Response from password reset attempt.
/// </summary>
public record ResetPasswordResponse(
    bool Success,
    string? Error = null
);

// =============================================================================
// Verify Email
// =============================================================================

/// <summary>
/// Request to verify email address.
/// </summary>
public class VerifyEmailRequest
{
    [Required]
    public string UserId { get; set; } = "";

    [Required]
    public string Token { get; set; } = "";
}

/// <summary>
/// Response from email verification attempt.
/// </summary>
public record VerifyEmailResponse(
    bool Success,
    string Message
);

// =============================================================================
// Resend Verification
// =============================================================================

/// <summary>
/// Response from resend verification email request.
/// </summary>
public record ResendVerificationResponse(
    bool Success,
    string? Error = null,
    string? Message = null
);

// =============================================================================
// Current User (Get Me)
// =============================================================================

/// <summary>
/// Response containing current user information.
/// </summary>
public record GetMeResponse(
    bool IsAuthenticated,
    UserDto? User = null
);

/// <summary>
/// User information returned in auth responses.
/// </summary>
public record UserDto(
    int Id,
    string Email,
    string? DisplayName,
    bool EmailVerified,
    bool IsAdmin,
    string? AvatarUrl = null
);

// =============================================================================
// Logout
// =============================================================================

/// <summary>
/// Response from logout.
/// </summary>
public record LogoutResponse(
    bool Success
);
