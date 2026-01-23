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
    [Required]
    public string NewRole { get; set; } = "";
}

/// <summary>
/// Response from role change.
/// </summary>
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
    public int? LockDays { get; set; }
}

/// <summary>
/// Response from lock/unlock operation.
/// </summary>
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
    [Required(ErrorMessage = "Please confirm the email address")]
    public string ConfirmEmail { get; set; } = "";
}

/// <summary>
/// Response from user deletion.
/// </summary>
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
public record ImpersonateResponse(
    bool Success,
    string? Error = null
);

/// <summary>
/// Response from impersonation stop.
/// </summary>
public record StopImpersonationResponse(
    bool Success
);

/// <summary>
/// Response indicating if currently impersonating.
/// </summary>
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
public record AdminProjectDto(
    int Id,
    string Name,
    string? Description,
    string OwnerEmail,
    string OwnerDisplayName,
    int OwnerId,
    DateTime CreatedAt,
    int BuildCount,
    DateTime? LastBuildAt
);
