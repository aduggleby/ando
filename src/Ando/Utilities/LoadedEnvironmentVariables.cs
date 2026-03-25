// =============================================================================
// LoadedEnvironmentVariables.cs
//
// Summary: Tracks environment variables loaded by ANDO from .env files.
//
// This utility records the names of environment variables that ANDO loaded from
// `.env.ando` or `.env` so they can be forwarded into containerized commands.
// This keeps the forwarding scope narrow: only env vars ANDO explicitly loaded
// are replayed into `docker exec`, rather than the entire host environment.
//
// Design Decisions:
// - Track names in a dedicated environment variable so child ANDO processes
//   inherit the metadata automatically
// - Store only keys, not values, so the canonical values remain in the process
//   environment and are read lazily when commands execute
// - Deduplicate keys to keep forwarding stable across nested builds
// =============================================================================

namespace Ando.Utilities;

/// <summary>
/// Tracks environment variables loaded by ANDO from env files.
/// </summary>
public static class LoadedEnvironmentVariables
{
    /// <summary>
    /// Environment variable used to store the tracked key list.
    /// </summary>
    public const string TrackedKeysEnvVar = "ANDO_LOADED_ENV_KEYS";

    /// <summary>
    /// Adds the provided environment variable names to the tracked key list.
    /// </summary>
    /// <param name="keys">Environment variable names to track.</param>
    public static void Track(IEnumerable<string> keys)
    {
        var trackedKeys = new HashSet<string>(GetTrackedKeys(), StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                trackedKeys.Add(key);
            }
        }

        var serializedKeys = string.Join('\n', trackedKeys.OrderBy(key => key, StringComparer.Ordinal));
        Environment.SetEnvironmentVariable(TrackedKeysEnvVar, serializedKeys);
    }

    /// <summary>
    /// Gets the currently tracked environment variable names.
    /// </summary>
    /// <returns>Tracked environment variable names.</returns>
    public static IReadOnlyList<string> GetTrackedKeys()
    {
        var serializedKeys = Environment.GetEnvironmentVariable(TrackedKeysEnvVar);
        if (string.IsNullOrWhiteSpace(serializedKeys))
        {
            return [];
        }

        return serializedKeys
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(key => key, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// Gets tracked environment variables and their current values.
    /// </summary>
    /// <returns>Tracked variables that currently have non-null values.</returns>
    public static IReadOnlyDictionary<string, string> GetTrackedVariables()
    {
        var variables = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in GetTrackedKeys())
        {
            var value = Environment.GetEnvironmentVariable(key);
            if (value != null)
            {
                variables[key] = value;
            }
        }

        return variables;
    }
}
