// =============================================================================
// VarsContext.cs
//
// Summary: Manages build-time variables and environment access for build scripts.
//
// VarsContext provides a dictionary-like interface for storing and retrieving
// build variables, plus safe environment variable access with secret tracking.
// Variables set here can be used across build steps.
//
// Design Decisions:
// - Indexer syntax (vars["key"]) for intuitive access like a dictionary
// - Null assignment removes the key to support conditional unsetting
// - EnvRequired marks variables as secrets to prevent accidental logging
// - Secrets tracking allows the logger to redact sensitive values
// - Separate from PathsContext to maintain single responsibility
// =============================================================================

namespace Ando.Context;

/// <summary>
/// Manages build-time variables and environment access for build scripts.
/// Provides dictionary-like access with secret tracking for sensitive values.
/// </summary>
public class VarsContext
{
    // Build variables set during script execution.
    private readonly Dictionary<string, string> _vars = [];

    // Tracks which environment variables are secrets (for log redaction).
    private readonly HashSet<string> _secrets = [];

    // Tracks the actual secret values (for log redaction).
    private readonly HashSet<string> _secretValues = [];

    /// <summary>
    /// Gets or sets a build variable. Setting to null removes the variable.
    /// </summary>
    /// <param name="key">The variable name.</param>
    public string? this[string key]
    {
        get => _vars.TryGetValue(key, out var value) ? value : null;
        set
        {
            // Setting null removes the key, allowing conditional cleanup.
            if (value == null)
            {
                _vars.Remove(key);
            }
            else
            {
                _vars[key] = value;
            }
        }
    }

    /// <summary>
    /// Gets an environment variable value, or null if not set.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    public string? Env(string name)
    {
        return Environment.GetEnvironmentVariable(name);
    }

    /// <summary>
    /// Gets a required environment variable, throwing if not set.
    /// Also marks the variable as a secret to prevent logging.
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <exception cref="InvalidOperationException">Thrown when the variable is not set.</exception>
    public string EnvRequired(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
        }
        // Mark as secret - typically used for tokens, passwords, etc.
        // The logger can use this to redact values from output.
        _secrets.Add(name);
        _secretValues.Add(value);
        return value;
    }

    /// <summary>
    /// Checks if a build variable is defined.
    /// </summary>
    public bool Has(string key) => _vars.ContainsKey(key);

    /// <summary>
    /// Returns all build variables as a read-only dictionary.
    /// </summary>
    public IReadOnlyDictionary<string, string> All() => _vars;

    /// <summary>
    /// Checks if an environment variable was accessed via EnvRequired (is a secret).
    /// Used by the logger to determine if values should be redacted.
    /// </summary>
    public bool IsSecret(string name) => _secrets.Contains(name);

    /// <summary>
    /// Gets all secret values for redaction purposes.
    /// Used by the logger to redact sensitive values from output.
    /// </summary>
    public IReadOnlySet<string> GetSecretValues() => _secretValues;

    /// <summary>
    /// Manually registers a value as a secret for redaction.
    /// Use this for secrets obtained from sources other than environment variables.
    /// </summary>
    /// <param name="value">The secret value to redact from logs.</param>
    public void RegisterSecret(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            _secretValues.Add(value);
        }
    }
}
