// =============================================================================
// Profile.cs
//
// Summary: Represents a build profile for conditional step execution.
//
// Profiles allow build scripts to define conditional sections that only run
// when the profile is activated via CLI (e.g., ando -p release). The Profile
// class has an implicit bool conversion so it can be used directly in if statements.
//
// Example usage in build.csando:
//   var release = DefineProfile("release");
//   if (release) { Git.Tag(version); }
//
// Design Decisions:
// - Implicit bool conversion for clean if (profile) syntax
// - Profiles are defined in scripts and validated against CLI args before execution
// - Names are case-insensitive for user convenience
// =============================================================================

namespace Ando.Profiles;

/// <summary>
/// Represents a build profile that can be activated via CLI flags.
/// Use DefineProfile() in build scripts to create profiles.
/// </summary>
public class Profile
{
    /// <summary>
    /// The name of the profile (e.g., "release", "push").
    /// </summary>
    public string Name { get; }

    // Reference to the registry to check if this profile is active.
    private readonly ProfileRegistry _registry;

    internal Profile(string name, ProfileRegistry registry)
    {
        Name = name;
        _registry = registry;
    }

    /// <summary>
    /// Returns true if this profile was activated via CLI (e.g., -p release).
    /// </summary>
    public bool IsActive => _registry.IsActive(Name);

    /// <summary>
    /// Implicit conversion to bool for use in if statements.
    /// </summary>
    public static implicit operator bool(Profile profile) => profile.IsActive;

    public override string ToString() => Name;
}
