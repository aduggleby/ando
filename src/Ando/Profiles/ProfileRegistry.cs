// =============================================================================
// ProfileRegistry.cs
//
// Summary: Manages profile definitions and activation state.
//
// ProfileRegistry tracks which profiles are defined in build scripts and which
// are activated via CLI. It provides validation to catch typos in profile names
// before the workflow runs.
//
// Flow:
// 1. CLI parses -p/--profile flags and passes active profiles to registry
// 2. Script executes DefineProfile() calls which register valid profile names
// 3. Before workflow runs, Validate() checks all active profiles are defined
// 4. During execution, IsActive() checks if a profile was requested
//
// Design Decisions:
// - Case-insensitive matching for user convenience
// - Validation before execution prevents typos from silently being ignored
// - Clear error messages list all available profiles
// =============================================================================

namespace Ando.Profiles;

/// <summary>
/// Registry for managing build profiles.
/// Tracks defined profiles (from script) and active profiles (from CLI).
/// </summary>
public class ProfileRegistry
{
    // Profiles defined in the build script via DefineProfile().
    private readonly HashSet<string> _definedProfiles = new(StringComparer.OrdinalIgnoreCase);

    // Profiles activated via CLI (-p or --profile flags).
    private readonly HashSet<string> _activeProfiles = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the names of all defined profiles.
    /// </summary>
    public IReadOnlyCollection<string> DefinedProfiles => _definedProfiles;

    /// <summary>
    /// Gets the names of all active (CLI-requested) profiles.
    /// </summary>
    public IReadOnlyCollection<string> ActiveProfiles => _activeProfiles;

    /// <summary>
    /// Sets the active profiles from CLI arguments.
    /// Call this before loading the build script.
    /// </summary>
    /// <param name="profiles">Profile names from CLI (e.g., from -p push,release).</param>
    public void SetActiveProfiles(IEnumerable<string> profiles)
    {
        _activeProfiles.Clear();
        foreach (var profile in profiles)
        {
            _activeProfiles.Add(profile);
        }
    }

    /// <summary>
    /// Defines a profile in the build script.
    /// Returns a Profile object that can be used in conditionals.
    /// </summary>
    /// <param name="name">The profile name.</param>
    /// <returns>A Profile instance for use in if statements.</returns>
    public Profile Define(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Profile name cannot be empty.", nameof(name));
        }

        _definedProfiles.Add(name);
        return new Profile(name, this);
    }

    /// <summary>
    /// Checks if a profile is currently active (was passed via CLI).
    /// </summary>
    /// <param name="name">The profile name to check.</param>
    /// <returns>True if the profile is active.</returns>
    public bool IsActive(string name) => _activeProfiles.Contains(name);

    /// <summary>
    /// Validates that all active profiles were defined in the script.
    /// Call this after script loading but before workflow execution.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when an active profile was not defined in the script.
    /// </exception>
    public void Validate()
    {
        var unknownProfiles = _activeProfiles
            .Where(p => !_definedProfiles.Contains(p))
            .ToList();

        if (unknownProfiles.Count > 0)
        {
            var unknown = string.Join(", ", unknownProfiles.Select(p => $"'{p}'"));
            var available = _definedProfiles.Count > 0
                ? string.Join(", ", _definedProfiles.OrderBy(p => p))
                : "(none defined)";

            throw new InvalidOperationException(
                $"Unknown profile(s): {unknown}. Available profiles: {available}");
        }
    }
}
