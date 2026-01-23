// =============================================================================
// ApplicationRole.cs
//
// Summary: ASP.NET Core Identity role entity for Ando CI.
//
// Defines application roles with optional descriptions for documentation.
// The system uses two primary roles: Admin (full system control) and User
// (standard access).
//
// Design Decisions:
// - Inherits from IdentityRole<int> for integer primary keys
// - Description field for admin UI documentation
// - Roles seeded on application startup
// =============================================================================

using Microsoft.AspNetCore.Identity;

namespace Ando.Server.Models;

/// <summary>
/// Application role with optional description.
/// </summary>
public class ApplicationRole : IdentityRole<int>
{
    /// <summary>
    /// Human-readable description of what this role allows.
    /// </summary>
    public string? Description { get; set; }
}
