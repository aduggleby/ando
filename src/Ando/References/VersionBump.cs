// =============================================================================
// VersionBump.cs
//
// Summary: Enum for specifying version bump levels.
//
// Used by BumpVersion operations to specify which part of a semantic version
// to increment (major.minor.patch).
//
// Design Decisions:
// - Follows semantic versioning conventions
// - Patch is the default/safest bump for most use cases
// =============================================================================

namespace Ando.References;

/// <summary>
/// Specifies which component of a semantic version to increment.
/// </summary>
public enum VersionBump
{
    /// <summary>
    /// Increment the patch version (1.0.0 → 1.0.1).
    /// Use for bug fixes and minor changes.
    /// </summary>
    Patch,

    /// <summary>
    /// Increment the minor version and reset patch (1.0.5 → 1.1.0).
    /// Use for new features that are backwards compatible.
    /// </summary>
    Minor,

    /// <summary>
    /// Increment the major version and reset minor/patch (1.5.3 → 2.0.0).
    /// Use for breaking changes.
    /// </summary>
    Major
}
