// =============================================================================
// UserRoles.cs
//
// Summary: Static constants for application role names.
//
// Provides compile-time safe role name constants to avoid magic strings
// throughout the codebase. Use these constants in [Authorize] attributes
// and role checks.
//
// Design Decisions:
// - Static class with const strings for compile-time safety
// - Two roles: Admin (full control) and User (standard access)
// - First registered user automatically becomes Admin
// =============================================================================

namespace Ando.Server.Models;

/// <summary>
/// Application role name constants.
/// </summary>
public static class UserRoles
{
    /// <summary>
    /// Global system administrator with full control.
    /// Can manage all users, view all projects, impersonate users.
    /// </summary>
    public const string Admin = "Admin";

    /// <summary>
    /// Standard user with access to their own projects.
    /// Can create projects, manage builds, configure settings.
    /// </summary>
    public const string User = "User";

    /// <summary>
    /// All available roles for iteration.
    /// </summary>
    public static readonly string[] AllRoles = [Admin, User];
}
