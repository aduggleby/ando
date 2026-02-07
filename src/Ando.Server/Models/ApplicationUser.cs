// =============================================================================
// ApplicationUser.cs
//
// Summary: ASP.NET Core Identity user entity for Ando CI.
//
// Extends IdentityUser to include profile information, soft email verification,
// and optional GitHub connection for repository access. GitHub is no longer
// required for authentication - only for connecting projects to repositories.
//
// Design Decisions:
// - Inherits from IdentityUser<int> for integer primary keys (consistency with existing schema)
// - Soft email verification: users can log in but see reminder banner until verified
// - GitHub connection is optional and separate from authentication
// - First registered user automatically becomes global admin
// =============================================================================

using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Models;

/// <summary>
/// Application user with email/password authentication and optional GitHub connection.
/// </summary>
public class ApplicationUser : IdentityUser<int>
{
    /// <summary>
    /// Display name shown in the UI. Defaults to email prefix if not set.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// URL to user's avatar image. Can be from GitHub or uploaded.
    /// </summary>
    public string? AvatarUrl { get; set; }

    // -------------------------------------------------------------------------
    // Soft Email Verification
    // -------------------------------------------------------------------------

    /// <summary>
    /// Whether the user has verified their email address.
    /// Users can still log in without verification but see a reminder banner.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// When the last verification email was sent.
    /// Used to prevent spamming the resend button.
    /// </summary>
    public DateTime? EmailVerificationSentAt { get; set; }

    /// <summary>
    /// Token for email verification link. Cleared after successful verification.
    /// </summary>
    public string? EmailVerificationToken { get; set; }

    // -------------------------------------------------------------------------
    // Optional GitHub Connection (for repository access, not authentication)
    // -------------------------------------------------------------------------

    /// <summary>
    /// GitHub's unique user ID. Null if GitHub not connected.
    /// </summary>
    public long? GitHubId { get; set; }

    /// <summary>
    /// GitHub username. Can change if user renames their GitHub account.
    /// </summary>
    public string? GitHubLogin { get; set; }

    /// <summary>
    /// Encrypted OAuth access token for GitHub API access.
    /// Used for listing repositories and creating projects.
    /// </summary>
    public string? GitHubAccessToken { get; set; }

    /// <summary>
    /// When the GitHub account was connected.
    /// </summary>
    public DateTime? GitHubConnectedAt { get; set; }

    // -------------------------------------------------------------------------
    // Timestamps
    // -------------------------------------------------------------------------

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the user last logged in.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    // -------------------------------------------------------------------------
    // Navigation Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Projects owned by this user.
    /// </summary>
    public ICollection<Project> Projects { get; set; } = [];

    /// <summary>
    /// API tokens created by this user.
    /// </summary>
    public ICollection<ApiToken> ApiTokens { get; set; } = [];

    // -------------------------------------------------------------------------
    // Helper Properties
    // -------------------------------------------------------------------------

    /// <summary>
    /// Gets the display name or falls back to email prefix.
    /// </summary>
    public string EffectiveDisplayName => DisplayName ?? Email?.Split('@')[0] ?? "User";

    /// <summary>
    /// Whether the user has connected a GitHub account.
    /// </summary>
    public bool HasGitHubConnection => GitHubId.HasValue;
}
