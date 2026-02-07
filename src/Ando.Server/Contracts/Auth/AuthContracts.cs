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
    /// <summary>
    /// User's email address.
    /// </summary>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = "";

    /// <summary>
    /// User's password.
    /// </summary>
    [Required(ErrorMessage = "Password is required")]
    public string Password { get; set; } = "";

    /// <summary>
    /// Whether to persist the session beyond the browser session.
    /// </summary>
    public bool RememberMe { get; set; }
}

/// <summary>
/// Response from login attempt.
/// </summary>
/// <param name="Success">Whether the login succeeded.</param>
/// <param name="Error">Error message if login failed.</param>
/// <param name="User">User information if login succeeded.</param>
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
    /// <summary>
    /// User's email address (will be used for login).
    /// </summary>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = "";

    /// <summary>
    /// Optional display name for the user.
    /// </summary>
    [StringLength(100, ErrorMessage = "Display name cannot exceed 100 characters")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// Password for the new account (minimum 8 characters).
    /// </summary>
    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = "";

    /// <summary>
    /// Password confirmation (must match Password).
    /// </summary>
    [Required(ErrorMessage = "Please confirm your password")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// Response from registration attempt.
/// </summary>
/// <param name="Success">Whether the registration succeeded.</param>
/// <param name="Error">Error message if registration failed.</param>
/// <param name="IsFirstUser">Whether this is the first user (gets admin role).</param>
/// <param name="User">User information if registration succeeded.</param>
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
    /// <summary>
    /// Email address of the account to reset password for.
    /// </summary>
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Invalid email address")]
    public string Email { get; set; } = "";
}

/// <summary>
/// Response from forgot password request.
/// Always returns success to prevent email enumeration.
/// </summary>
/// <param name="Success">Always true to prevent email enumeration.</param>
/// <param name="Message">Generic success message.</param>
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
    /// <summary>
    /// Email address of the account.
    /// </summary>
    [Required]
    public string Email { get; set; } = "";

    /// <summary>
    /// Password reset token from the email link.
    /// </summary>
    [Required]
    public string Token { get; set; } = "";

    /// <summary>
    /// New password (minimum 8 characters).
    /// </summary>
    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 8, ErrorMessage = "Password must be at least 8 characters")]
    public string Password { get; set; } = "";

    /// <summary>
    /// Password confirmation (must match Password).
    /// </summary>
    [Required(ErrorMessage = "Please confirm your password")]
    [Compare("Password", ErrorMessage = "Passwords do not match")]
    public string ConfirmPassword { get; set; } = "";
}

/// <summary>
/// Response from password reset attempt.
/// </summary>
/// <param name="Success">Whether the password was reset successfully.</param>
/// <param name="Error">Error message if reset failed.</param>
public record ResetPasswordResponse(
    bool Success,
    string? Error = null
);

// =============================================================================
// API Tokens
// =============================================================================

/// <summary>
/// Request to create a personal API token.
/// </summary>
public class CreateApiTokenRequest
{
    /// <summary>
    /// Friendly name for the token (e.g., "CI script", "Laptop").
    /// </summary>
    [Required(ErrorMessage = "Name is required")]
    [StringLength(100, MinimumLength = 2, ErrorMessage = "Name must be between 2 and 100 characters")]
    public string Name { get; set; } = "";
}

/// <summary>
/// API token metadata returned by list operations (never includes the raw token).
/// </summary>
public record ApiTokenDto(
    int Id,
    string Name,
    string Prefix,
    DateTime CreatedAtUtc,
    DateTime? LastUsedAtUtc,
    DateTime? RevokedAtUtc
);

/// <summary>
/// Response from token creation. The raw token is only returned once.
/// </summary>
public record CreateApiTokenResponse(
    bool Success,
    ApiTokenDto? Token = null,
    string? Value = null,
    string? Error = null
);

/// <summary>
/// Response containing a user's tokens.
/// </summary>
public record ListApiTokensResponse(
    IReadOnlyList<ApiTokenDto> Tokens
);

/// <summary>
/// Response from revocation attempt.
/// </summary>
public record RevokeApiTokenResponse(
    bool Success
);

// =============================================================================
// Verify Email
// =============================================================================

/// <summary>
/// Request to verify email address.
/// </summary>
public class VerifyEmailRequest
{
    /// <summary>
    /// ID of the user verifying their email.
    /// </summary>
    [Required]
    public string UserId { get; set; } = "";

    /// <summary>
    /// Verification token from the email link.
    /// </summary>
    [Required]
    public string Token { get; set; } = "";
}

/// <summary>
/// Response from email verification attempt.
/// </summary>
/// <param name="Success">Whether the email was verified successfully.</param>
/// <param name="Message">Message describing the result.</param>
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
/// <param name="Success">Whether the email was resent successfully.</param>
/// <param name="Error">Error message if resend failed.</param>
/// <param name="Message">Success message if applicable.</param>
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
/// <param name="IsAuthenticated">Whether the request is from an authenticated user.</param>
/// <param name="User">User information if authenticated.</param>
public record GetMeResponse(
    bool IsAuthenticated,
    UserDto? User = null
);

/// <summary>
/// User information returned in auth responses.
/// </summary>
/// <param name="Id">User's unique identifier.</param>
/// <param name="Email">User's email address.</param>
/// <param name="DisplayName">User's display name.</param>
/// <param name="EmailVerified">Whether the user's email is verified.</param>
/// <param name="IsAdmin">Whether the user has admin role.</param>
/// <param name="AvatarUrl">URL to user's avatar image.</param>
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
/// <param name="Success">Whether logout succeeded.</param>
public record LogoutResponse(
    bool Success
);
