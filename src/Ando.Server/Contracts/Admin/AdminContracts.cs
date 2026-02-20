// =============================================================================
// AdminContracts.cs
//
// Summary: Request and response DTOs for admin-only API endpoints.
//
// These contracts define the data exchanged for admin dashboard, user management,
// project overview, and impersonation features. All endpoints require admin role.
//
// Design Decisions:
// - Pagination support for user and project lists
// - Audit trail for sensitive operations through logging (not in DTOs)
// - Cannot perform certain operations on yourself (enforced in endpoints)
// =============================================================================

using System.ComponentModel.DataAnnotations;

namespace Ando.Server.Contracts.Admin;

// =============================================================================
// Dashboard
// =============================================================================

/// <summary>
/// Admin dashboard statistics and recent activity.
/// </summary>
/// <param name="TotalUsers">Total number of registered users.</param>
/// <param name="VerifiedUsers">Number of users with verified email addresses.</param>
/// <param name="UnverifiedUsers">Number of users with unverified email addresses.</param>
/// <param name="AdminUsers">Number of users with admin role.</param>
/// <param name="TotalProjects">Total number of projects across all users.</param>
/// <param name="TotalBuilds">Total number of builds ever created.</param>
/// <param name="RecentBuilds">Number of builds in the last 24 hours.</param>
/// <param name="RecentUsers">List of recently registered users.</param>
/// <param name="RecentBuilds24h">List of builds in the last 24 hours.</param>
public record AdminDashboardDto(
    int TotalUsers,
    int VerifiedUsers,
    int UnverifiedUsers,
    int AdminUsers,
    int TotalProjects,
    int TotalBuilds,
    int RecentBuilds,
    IReadOnlyList<RecentUserDto> RecentUsers,
    IReadOnlyList<RecentBuildDto> RecentBuilds24h
);

/// <summary>
/// Recent user for dashboard.
/// </summary>
/// <param name="Id">User's unique identifier.</param>
/// <param name="Email">User's email address.</param>
/// <param name="DisplayName">User's display name.</param>
/// <param name="CreatedAt">When the user account was created.</param>
/// <param name="EmailVerified">Whether the user has verified their email.</param>
public record RecentUserDto(
    int Id,
    string Email,
    string DisplayName,
    DateTime CreatedAt,
    bool EmailVerified
);

/// <summary>
/// Recent build for dashboard.
/// </summary>
/// <param name="Id">Build's unique identifier.</param>
/// <param name="ProjectName">Name of the project.</param>
/// <param name="Branch">Git branch that triggered the build.</param>
/// <param name="Status">Current build status.</param>
/// <param name="CreatedAt">When the build was created.</param>
public record RecentBuildDto(
    int Id,
    string ProjectName,
    string Branch,
    string Status,
    DateTime CreatedAt
);

// =============================================================================
// User List
// =============================================================================

/// <summary>
/// Response containing paginated user list.
/// </summary>
/// <param name="Users">List of users for the current page.</param>
/// <param name="CurrentPage">Current page number (1-based).</param>
/// <param name="TotalPages">Total number of pages available.</param>
/// <param name="TotalUsers">Total number of users matching the filter.</param>
/// <param name="PageSize">Number of users per page.</param>
/// <param name="SearchQuery">Current search query, if any.</param>
/// <param name="RoleFilter">Role filter applied, if any.</param>
public record GetUsersResponse(
    IReadOnlyList<UserListItemDto> Users,
    int CurrentPage,
    int TotalPages,
    int TotalUsers,
    int PageSize,
    string? SearchQuery,
    string? RoleFilter
);

/// <summary>
/// User summary for list view.
/// </summary>
/// <param name="Id">User's unique identifier.</param>
/// <param name="Email">User's email address.</param>
/// <param name="DisplayName">User's display name.</param>
/// <param name="EmailVerified">Whether the user has verified their email.</param>
/// <param name="IsAdmin">Whether the user has admin role.</param>
/// <param name="IsLockedOut">Whether the user account is locked.</param>
/// <param name="CreatedAt">When the user account was created.</param>
/// <param name="LastLoginAt">When the user last logged in.</param>
/// <param name="HasGitHubConnection">Whether the user has connected GitHub.</param>
/// <param name="ProjectCount">Number of projects owned by the user.</param>
public record UserListItemDto(
    int Id,
    string Email,
    string DisplayName,
    bool EmailVerified,
    bool IsAdmin,
    bool IsLockedOut,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool HasGitHubConnection,
    int ProjectCount
);

// =============================================================================
// User Details
// =============================================================================

/// <summary>
/// Full user details for admin view.
/// </summary>
/// <param name="Id">User's unique identifier.</param>
/// <param name="Email">User's email address.</param>
/// <param name="DisplayName">User's display name.</param>
/// <param name="AvatarUrl">URL to user's avatar image.</param>
/// <param name="EmailVerified">Whether the user has verified their email.</param>
/// <param name="EmailVerificationSentAt">When verification email was last sent.</param>
/// <param name="IsAdmin">Whether the user has admin role.</param>
/// <param name="IsLockedOut">Whether the user account is locked.</param>
/// <param name="LockoutEnd">When the lockout ends.</param>
/// <param name="CreatedAt">When the user account was created.</param>
/// <param name="LastLoginAt">When the user last logged in.</param>
/// <param name="HasGitHubConnection">Whether the user has connected GitHub.</param>
/// <param name="GitHubLogin">GitHub username if connected.</param>
/// <param name="GitHubConnectedAt">When GitHub was connected.</param>
/// <param name="Projects">List of projects owned by the user.</param>
/// <param name="TotalBuilds">Total number of builds across all projects.</param>
public record UserDetailsDto(
    int Id,
    string Email,
    string? DisplayName,
    string? AvatarUrl,
    bool EmailVerified,
    DateTime? EmailVerificationSentAt,
    bool IsAdmin,
    bool IsLockedOut,
    DateTimeOffset? LockoutEnd,
    DateTime CreatedAt,
    DateTime? LastLoginAt,
    bool HasGitHubConnection,
    string? GitHubLogin,
    DateTime? GitHubConnectedAt,
    IReadOnlyList<UserProjectDto> Projects,
    int TotalBuilds
);

/// <summary>
/// Project owned by a user (admin view).
/// </summary>
/// <param name="Id">Project's unique identifier.</param>
/// <param name="Name">Project name (repository name).</param>
/// <param name="Description">Optional project description.</param>
/// <param name="CreatedAt">When the project was created.</param>
/// <param name="BuildCount">Total number of builds for this project.</param>
public record UserProjectDto(
    int Id,
    string Name,
    string? Description,
    DateTime CreatedAt,
    int BuildCount
);

/// <summary>
/// Response containing user details.
/// </summary>
/// <param name="User">Full user details.</param>
public record GetUserDetailsResponse(
    UserDetailsDto User
);

// =============================================================================
// Change Role
// =============================================================================

/// <summary>
/// Request to change a user's role.
/// </summary>
public class ChangeUserRoleRequest
{
    /// <summary>
    /// The new role to assign to the user.
    /// </summary>
    [Required]
    public string NewRole { get; set; } = "";
}

/// <summary>
/// Response from role change.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Error">Error message if the operation failed.</param>
public record ChangeUserRoleResponse(
    bool Success,
    string? Error = null
);

// =============================================================================
// Lock/Unlock User
// =============================================================================

/// <summary>
/// Request to lock a user account.
/// </summary>
public class LockUserRequest
{
    /// <summary>
    /// Number of days to lock the account (1-365).
    /// </summary>
    public int? LockDays { get; set; }
}

/// <summary>
/// Response from lock/unlock operation.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Error">Error message if the operation failed.</param>
public record LockUserResponse(
    bool Success,
    string? Error = null
);

// =============================================================================
// Delete User
// =============================================================================

/// <summary>
/// Request to delete a user (requires email confirmation).
/// </summary>
public class DeleteUserRequest
{
    /// <summary>
    /// User's email address to confirm deletion.
    /// </summary>
    [Required(ErrorMessage = "Please confirm the email address")]
    public string ConfirmEmail { get; set; } = "";
}

/// <summary>
/// Response from user deletion.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Error">Error message if the operation failed.</param>
public record DeleteUserResponse(
    bool Success,
    string? Error = null
);

// =============================================================================
// Impersonation
// =============================================================================

/// <summary>
/// Response from impersonation start.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
/// <param name="Error">Error message if the operation failed.</param>
public record ImpersonateResponse(
    bool Success,
    string? Error = null
);

/// <summary>
/// Response from impersonation stop.
/// </summary>
/// <param name="Success">Whether the operation succeeded.</param>
public record StopImpersonationResponse(
    bool Success
);

/// <summary>
/// Response indicating if currently impersonating.
/// </summary>
/// <param name="IsImpersonating">Whether currently impersonating a user.</param>
/// <param name="OriginalUserId">ID of the original admin user.</param>
/// <param name="OriginalUserEmail">Email of the original admin user.</param>
public record ImpersonationStatusResponse(
    bool IsImpersonating,
    int? OriginalUserId = null,
    string? OriginalUserEmail = null
);

// =============================================================================
// Admin Projects List
// =============================================================================

/// <summary>
/// Response containing paginated admin project list.
/// </summary>
/// <param name="Projects">List of projects for the current page.</param>
/// <param name="CurrentPage">Current page number (1-based).</param>
/// <param name="TotalPages">Total number of pages available.</param>
/// <param name="TotalProjects">Total number of projects matching the filter.</param>
/// <param name="PageSize">Number of projects per page.</param>
/// <param name="SearchQuery">Current search query, if any.</param>
public record GetAdminProjectsResponse(
    IReadOnlyList<AdminProjectDto> Projects,
    int CurrentPage,
    int TotalPages,
    int TotalProjects,
    int PageSize,
    string? SearchQuery
);

/// <summary>
/// Project summary for admin list view.
/// </summary>
/// <param name="Id">Project's unique identifier.</param>
/// <param name="Name">Project name (repository name).</param>
/// <param name="Description">Optional project description.</param>
/// <param name="OwnerEmail">Email of the project owner.</param>
/// <param name="OwnerDisplayName">Display name of the project owner.</param>
/// <param name="OwnerId">ID of the project owner.</param>
/// <param name="CreatedAt">When the project was created.</param>
/// <param name="BuildCount">Total number of builds for this project.</param>
/// <param name="LastBuildAt">When the last build was started.</param>
/// <param name="LastBuildStatus">Status of the most recent build, if any.</param>
public record AdminProjectDto(
    int Id,
    string Name,
    string? Description,
    string OwnerEmail,
    string OwnerDisplayName,
    int OwnerId,
    DateTime CreatedAt,
    int BuildCount,
    DateTime? LastBuildAt,
    string? LastBuildStatus
);

// =============================================================================
// System Update
// =============================================================================

/// <summary>
/// Response containing server self-update status.
/// </summary>
/// <param name="Enabled">Whether self-update is enabled.</param>
/// <param name="IsChecking">Whether a background check is currently running.</param>
/// <param name="IsUpdateAvailable">Whether a newer image is available.</param>
/// <param name="IsUpdateInProgress">Whether an update workflow is currently running.</param>
/// <param name="CurrentImageId">Current running image ID.</param>
/// <param name="LatestImageId">Latest pulled image ID.</param>
/// <param name="CurrentVersion">Current image version label, if available.</param>
/// <param name="LatestVersion">Latest image version label, if available.</param>
/// <param name="LastCheckedAtUtc">Timestamp of the last update check.</param>
/// <param name="LastTriggeredAtUtc">Timestamp of the last admin-triggered update.</param>
/// <param name="LastError">Latest check/apply error, if any.</param>
public record SystemUpdateStatusResponse(
    bool Enabled,
    bool IsChecking,
    bool IsUpdateAvailable,
    bool IsUpdateInProgress,
    string? CurrentImageId,
    string? LatestImageId,
    string? CurrentVersion,
    string? LatestVersion,
    DateTime? LastCheckedAtUtc,
    DateTime? LastTriggeredAtUtc,
    string? LastError
);

/// <summary>
/// Response from an admin-triggered update request.
/// </summary>
/// <param name="Success">Whether the update job was queued.</param>
/// <param name="Message">Status message for UI display.</param>
/// <param name="JobId">Queued Hangfire job ID, if available.</param>
public record TriggerSystemUpdateResponse(
    bool Success,
    string Message,
    string? JobId = null
);

/// <summary>
/// Response containing live system health probe results.
/// </summary>
/// <param name="Checks">Individual health checks.</param>
/// <param name="CheckedAtUtc">Timestamp of the probe execution.</param>
public record SystemHealthResponse(
    IReadOnlyList<SystemHealthCheckDto> Checks,
    DateTime CheckedAtUtc
);

/// <summary>
/// A single health check result.
/// </summary>
/// <param name="Name">Display name of the subsystem.</param>
/// <param name="Status">Health status (`healthy`, `warning`, `error`).</param>
/// <param name="Message">Additional status context.</param>
public record SystemHealthCheckDto(
    string Name,
    string Status,
    string Message
);
