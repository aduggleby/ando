// =============================================================================
// AdminViewModels.cs
//
// Summary: View models for the admin section of the application.
//
// These view models support the admin dashboard, user management, and
// system administration features. Only users with the Admin role can
// access these pages.
// =============================================================================

using System.ComponentModel.DataAnnotations;
using Ando.Server.Models;

namespace Ando.Server.ViewModels;

/// <summary>
/// Dashboard view model showing system statistics.
/// </summary>
public class AdminDashboardViewModel
{
    /// <summary>
    /// Total number of registered users in the system.
    /// </summary>
    public int TotalUsers { get; set; }

    /// <summary>
    /// Number of users who have verified their email address.
    /// </summary>
    public int VerifiedUsers { get; set; }

    /// <summary>
    /// Number of users with unverified email addresses.
    /// </summary>
    public int UnverifiedUsers { get; set; }

    /// <summary>
    /// Number of users with the Admin role.
    /// </summary>
    public int AdminUsers { get; set; }

    /// <summary>
    /// Total number of projects across all users.
    /// </summary>
    public int TotalProjects { get; set; }

    /// <summary>
    /// Total number of builds ever created.
    /// </summary>
    public int TotalBuilds { get; set; }

    /// <summary>
    /// Number of builds created in the last 24 hours.
    /// </summary>
    public int RecentBuilds { get; set; }

    /// <summary>
    /// List of the most recently registered users.
    /// </summary>
    public List<RecentUserViewModel> RecentUsers { get; set; } = [];

    /// <summary>
    /// List of builds created in the last 24 hours.
    /// </summary>
    public List<RecentBuildViewModel> RecentBuilds24h { get; set; } = [];
}

/// <summary>
/// Simplified user info for dashboard lists.
/// </summary>
public class RecentUserViewModel
{
    /// <summary>
    /// Unique identifier for the user.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// User's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// User's display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Whether the user has verified their email address.
    /// </summary>
    public bool EmailVerified { get; set; }
}

/// <summary>
/// Simplified build info for dashboard lists.
/// </summary>
public class RecentBuildViewModel
{
    /// <summary>
    /// Unique identifier for the build.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Name of the project this build belongs to.
    /// </summary>
    public string ProjectName { get; set; } = string.Empty;

    /// <summary>
    /// Git branch that triggered the build.
    /// </summary>
    public string Branch { get; set; } = string.Empty;

    /// <summary>
    /// Current status of the build (e.g., Queued, Running, Completed, Failed).
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// When the build was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Paginated list of users for the admin users page.
/// </summary>
public class UserListViewModel
{
    /// <summary>
    /// List of users for the current page.
    /// </summary>
    public List<UserListItemViewModel> Users { get; set; } = [];

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Total number of pages available.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Total number of users matching the current filter.
    /// </summary>
    public int TotalUsers { get; set; }

    /// <summary>
    /// Number of users per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Current search query for filtering users, if any.
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// Role filter applied to the list (e.g., "Admin", "User").
    /// </summary>
    public string? RoleFilter { get; set; }
}

/// <summary>
/// User item in the admin users list.
/// </summary>
public class UserListItemViewModel
{
    /// <summary>
    /// Unique identifier for the user.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// User's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// User's display name.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user has verified their email address.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// Whether the user has the Admin role.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Whether the user account is currently locked out.
    /// </summary>
    public bool IsLockedOut { get; set; }

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the user last logged in, if ever.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Whether the user has connected a GitHub account.
    /// </summary>
    public bool HasGitHubConnection { get; set; }

    /// <summary>
    /// Number of projects owned by the user.
    /// </summary>
    public int ProjectCount { get; set; }
}

/// <summary>
/// Detailed user view for the admin user details page.
/// </summary>
public class UserDetailsViewModel
{
    /// <summary>
    /// Unique identifier for the user.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// User's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// User's display name, if set.
    /// </summary>
    public string? DisplayName { get; set; }

    /// <summary>
    /// URL to the user's avatar image, if available.
    /// </summary>
    public string? AvatarUrl { get; set; }

    /// <summary>
    /// Whether the user has verified their email address.
    /// </summary>
    public bool EmailVerified { get; set; }

    /// <summary>
    /// When the email verification link was last sent.
    /// </summary>
    public DateTime? EmailVerificationSentAt { get; set; }

    /// <summary>
    /// Whether the user has the Admin role.
    /// </summary>
    public bool IsAdmin { get; set; }

    /// <summary>
    /// Whether the user account is currently locked out.
    /// </summary>
    public bool IsLockedOut { get; set; }

    /// <summary>
    /// When the lockout ends, if the account is locked.
    /// </summary>
    public DateTimeOffset? LockoutEnd { get; set; }

    /// <summary>
    /// When the user account was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// When the user last logged in, if ever.
    /// </summary>
    public DateTime? LastLoginAt { get; set; }

    /// <summary>
    /// Whether the user has connected a GitHub account.
    /// </summary>
    public bool HasGitHubConnection { get; set; }

    /// <summary>
    /// GitHub username of the connected account.
    /// </summary>
    public string? GitHubLogin { get; set; }

    /// <summary>
    /// When the GitHub account was connected.
    /// </summary>
    public DateTime? GitHubConnectedAt { get; set; }

    /// <summary>
    /// List of projects owned by the user.
    /// </summary>
    public List<UserProjectViewModel> Projects { get; set; } = [];

    /// <summary>
    /// Total number of builds across all user's projects.
    /// </summary>
    public int TotalBuilds { get; set; }
}

/// <summary>
/// Simplified project info for user details page.
/// </summary>
public class UserProjectViewModel
{
    /// <summary>
    /// Unique identifier for the project.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Name of the project (typically the repository name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the project.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// When the project was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Total number of builds for this project.
    /// </summary>
    public int BuildCount { get; set; }
}

/// <summary>
/// Form model for changing a user's role.
/// </summary>
public class ChangeUserRoleViewModel
{
    /// <summary>
    /// ID of the user whose role is being changed.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Email address of the user (for display purposes).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// The user's current role.
    /// </summary>
    public string CurrentRole { get; set; } = string.Empty;

    /// <summary>
    /// The new role to assign to the user.
    /// </summary>
    [Required(ErrorMessage = "Please select a role")]
    public string NewRole { get; set; } = string.Empty;

    /// <summary>
    /// List of available roles that can be assigned.
    /// </summary>
    public List<string> AvailableRoles { get; set; } = [];
}

/// <summary>
/// Form model for locking/unlocking a user account.
/// </summary>
public class LockUserViewModel
{
    /// <summary>
    /// ID of the user to lock or unlock.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Email address of the user (for display purposes).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Whether the user account is currently locked.
    /// </summary>
    public bool IsCurrentlyLocked { get; set; }

    /// <summary>
    /// When the current lockout ends, if locked.
    /// </summary>
    public DateTimeOffset? CurrentLockoutEnd { get; set; }

    /// <summary>
    /// Number of days to lock the account for.
    /// </summary>
    [Range(1, 365, ErrorMessage = "Lock duration must be between 1 and 365 days")]
    public int? LockDays { get; set; }
}

/// <summary>
/// Confirmation model for deleting a user.
/// </summary>
public class DeleteUserViewModel
{
    /// <summary>
    /// ID of the user to delete.
    /// </summary>
    public int UserId { get; set; }

    /// <summary>
    /// Email address of the user (for confirmation matching).
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the user.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Number of projects that will be deleted with the user.
    /// </summary>
    public int ProjectCount { get; set; }

    /// <summary>
    /// Number of builds that will be deleted with the user.
    /// </summary>
    public int BuildCount { get; set; }

    /// <summary>
    /// User must type the email address to confirm deletion.
    /// </summary>
    [Required(ErrorMessage = "Please type the email address to confirm deletion")]
    [Compare(nameof(Email), ErrorMessage = "Email address does not match")]
    public string ConfirmEmail { get; set; } = string.Empty;
}

/// <summary>
/// Paginated list of all projects for admin view.
/// </summary>
public class AdminProjectListViewModel
{
    /// <summary>
    /// List of projects for the current page.
    /// </summary>
    public List<AdminProjectItemViewModel> Projects { get; set; } = [];

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int CurrentPage { get; set; }

    /// <summary>
    /// Total number of pages available.
    /// </summary>
    public int TotalPages { get; set; }

    /// <summary>
    /// Total number of projects matching the current filter.
    /// </summary>
    public int TotalProjects { get; set; }

    /// <summary>
    /// Number of projects per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Current search query for filtering projects, if any.
    /// </summary>
    public string? SearchQuery { get; set; }
}

/// <summary>
/// Project item in the admin projects list.
/// </summary>
public class AdminProjectItemViewModel
{
    /// <summary>
    /// Unique identifier for the project.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Name of the project (typically the repository name).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Optional description of the project.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Email address of the project owner.
    /// </summary>
    public string OwnerEmail { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the project owner.
    /// </summary>
    public string OwnerDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// ID of the project owner.
    /// </summary>
    public int OwnerId { get; set; }

    /// <summary>
    /// When the project was created.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Total number of builds for this project.
    /// </summary>
    public int BuildCount { get; set; }

    /// <summary>
    /// When the last build was started, if any.
    /// </summary>
    public DateTime? LastBuildAt { get; set; }
}
