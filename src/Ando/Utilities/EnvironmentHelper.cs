// =============================================================================
// EnvironmentHelper.cs
//
// Summary: Shared utilities for environment variable access.
//
// This class extracts common environment variable patterns used across multiple
// operations classes, eliminating duplication and providing consistent behavior.
//
// Design Decisions:
// - Static class since these are pure utility functions with no state
// - Throws InvalidOperationException for missing required variables (consistent with VarsContext)
// - Provides optional description parameter for clearer error messages
// =============================================================================

namespace Ando.Utilities;

/// <summary>
/// Shared utilities for environment variable access.
/// Provides consistent error handling for required environment variables.
/// </summary>
public static class EnvironmentHelper
{
    /// <summary>
    /// Gets a required environment variable, throwing if not set.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <param name="description">Optional description for clearer error messages.</param>
    /// <exception cref="InvalidOperationException">Thrown when the variable is not set.</exception>
    public static string GetRequired(string name, string? description = null)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            var message = description != null
                ? $"Required environment variable '{name}' ({description}) is not set."
                : $"Required environment variable '{name}' is not set.";
            throw new InvalidOperationException(message);
        }
        return value;
    }

    /// <summary>
    /// Gets an environment variable value, or null if not set.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    public static string? Get(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    /// <summary>
    /// Gets an environment variable with a default value if not set.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <param name="defaultValue">Value to return if not set.</param>
    public static string GetOrDefault(string name, string defaultValue)
    {
        var value = Environment.GetEnvironmentVariable(name);
        return string.IsNullOrEmpty(value) ? defaultValue : value;
    }
}
