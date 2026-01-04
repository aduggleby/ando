// =============================================================================
// User.cs
//
// Summary: Represents a user account linked to GitHub OAuth.
//
// Users authenticate via GitHub OAuth and can own multiple projects. Each user
// has a unique GitHub ID that serves as the primary identifier for login.
//
// Design Decisions:
// - AccessToken is encrypted at rest using AES-256
// - GitHubId is the authoritative identifier (immutable after creation)
// - Email is optional as some GitHub accounts don't expose email
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// A user account authenticated via GitHub OAuth.
/// </summary>
public class User
{
    public int Id { get; set; }

    /// <summary>
    /// GitHub's unique user ID (immutable).
    /// </summary>
    public long GitHubId { get; set; }

    /// <summary>
    /// GitHub username (may change, but GitHubId is authoritative).
    /// </summary>
    public string GitHubLogin { get; set; } = "";

    /// <summary>
    /// User's email address for notifications. May be null if GitHub doesn't expose it.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// URL to the user's GitHub avatar image.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Encrypted GitHub OAuth access token for API calls.
    /// </summary>
    public string? AccessToken { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Projects owned by this user.
    /// </summary>
    public ICollection<Project> Projects { get; set; } = [];
}
