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
    public int TotalUsers { get; set; }
    public int VerifiedUsers { get; set; }
    public int UnverifiedUsers { get; set; }
    public int AdminUsers { get; set; }
    public int TotalProjects { get; set; }
    public int TotalBuilds { get; set; }
    public int RecentBuilds { get; set; }
    public List<RecentUserViewModel> RecentUsers { get; set; } = [];
    public List<RecentBuildViewModel> RecentBuilds24h { get; set; } = [];
}

/// <summary>
/// Simplified user info for dashboard lists.
/// </summary>
public class RecentUserViewModel
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public bool EmailVerified { get; set; }
}

/// <summary>
/// Simplified build info for dashboard lists.
/// </summary>
public class RecentBuildViewModel
{
    public int Id { get; set; }
    public string ProjectName { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Paginated list of users for the admin users page.
/// </summary>
public class UserListViewModel
{
    public List<UserListItemViewModel> Users { get; set; } = [];
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalUsers { get; set; }
    public int PageSize { get; set; }
    public string? SearchQuery { get; set; }
    public string? RoleFilter { get; set; }
}

/// <summary>
/// User item in the admin users list.
/// </summary>
public class UserListItemViewModel
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool EmailVerified { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool HasGitHubConnection { get; set; }
    public int ProjectCount { get; set; }
}

/// <summary>
/// Detailed user view for the admin user details page.
/// </summary>
public class UserDetailsViewModel
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? AvatarUrl { get; set; }
    public bool EmailVerified { get; set; }
    public DateTime? EmailVerificationSentAt { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsLockedOut { get; set; }
    public DateTimeOffset? LockoutEnd { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }

    // GitHub connection
    public bool HasGitHubConnection { get; set; }
    public string? GitHubLogin { get; set; }
    public DateTime? GitHubConnectedAt { get; set; }

    // Related data
    public List<UserProjectViewModel> Projects { get; set; } = [];
    public int TotalBuilds { get; set; }
}

/// <summary>
/// Simplified project info for user details page.
/// </summary>
public class UserProjectViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int BuildCount { get; set; }
}

/// <summary>
/// Form model for changing a user's role.
/// </summary>
public class ChangeUserRoleViewModel
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string CurrentRole { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please select a role")]
    public string NewRole { get; set; } = string.Empty;

    public List<string> AvailableRoles { get; set; } = [];
}

/// <summary>
/// Form model for locking/unlocking a user account.
/// </summary>
public class LockUserViewModel
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public bool IsCurrentlyLocked { get; set; }
    public DateTimeOffset? CurrentLockoutEnd { get; set; }

    [Range(1, 365, ErrorMessage = "Lock duration must be between 1 and 365 days")]
    public int? LockDays { get; set; }
}

/// <summary>
/// Confirmation model for deleting a user.
/// </summary>
public class DeleteUserViewModel
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public int ProjectCount { get; set; }
    public int BuildCount { get; set; }

    [Required(ErrorMessage = "Please type the email address to confirm deletion")]
    [Compare(nameof(Email), ErrorMessage = "Email address does not match")]
    public string ConfirmEmail { get; set; } = string.Empty;
}

/// <summary>
/// Paginated list of all projects for admin view.
/// </summary>
public class AdminProjectListViewModel
{
    public List<AdminProjectItemViewModel> Projects { get; set; } = [];
    public int CurrentPage { get; set; }
    public int TotalPages { get; set; }
    public int TotalProjects { get; set; }
    public int PageSize { get; set; }
    public string? SearchQuery { get; set; }
}

/// <summary>
/// Project item in the admin projects list.
/// </summary>
public class AdminProjectItemViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string OwnerEmail { get; set; } = string.Empty;
    public string OwnerDisplayName { get; set; } = string.Empty;
    public int OwnerId { get; set; }
    public DateTime CreatedAt { get; set; }
    public int BuildCount { get; set; }
    public DateTime? LastBuildAt { get; set; }
}
