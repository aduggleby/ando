// =============================================================================
// SecretValues.cs
//
// Summary: Process-wide set of secret values redacted from all output.
//
// ANDO runs one build per process, so a static set lets every part of the CLI
// (the console logger, the build log file, run records) redact the same values
// without passing a registry around. Values come from environment variables
// whose names look secret (AZURE_CLIENT_SECRET, *_TOKEN, *_PASSWORD, ...) and
// from operations that receive a secret explicitly.
// =============================================================================

namespace Ando.Logging;

/// <summary>
/// Secret values that must never appear in console output, logs or run records.
/// </summary>
public static class SecretValues
{
    private static readonly object Lock = new();
    private static readonly HashSet<string> Values = [];

    // Values shorter than this are not redacted, to avoid mangling ordinary text.
    private const int MinLength = 6;

    private static readonly string[] SecretNameParts =
        ["SECRET", "PASSWORD", "PASSWD", "TOKEN", "APIKEY", "API_KEY", "PRIVATE_KEY", "CONNECTIONSTRING",
         "CONNECTION_STRING", "CREDENTIAL", "_PAT", "_KEY"];

    /// <summary>Adds a secret value.</summary>
    public static void Add(string? value)
    {
        if (string.IsNullOrEmpty(value)) return;
        // A dry-run placeholder ("<NAME>") is not a secret; redacting it would hide it in the tree.
        if (Ando.Utilities.DryRunEnvironment.IsAnyPlaceholder(value)) return;
        lock (Lock)
        {
            if (value.Length >= MinLength) Values.Add(value);

            // A multi-line value (a PEM key) is also redacted line by line, so a single
            // line printed on its own does not leak.
            if (value.Contains('\n'))
            {
                foreach (var line in value.Split('\n'))
                {
                    var trimmed = line.TrimEnd('\r');
                    if (trimmed.Length >= MinLength && !string.IsNullOrWhiteSpace(trimmed)) Values.Add(trimmed);
                }
            }
        }
    }

    /// <summary>Whether an environment variable name looks like it holds a secret.</summary>
    public static bool IsSecretName(string name)
    {
        var upper = name.ToUpperInvariant();
        // Match whole name tokens (NUGET_API_KEY, GH_PAT) so NODE_PATH or
        // LD_LIBRARY_PATH are not mistaken for secrets.
        var tokens = upper.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return SecretNameParts.Any(part => part.StartsWith('_')
                   ? tokens.Contains(part[1..])
                   : upper.Contains(part))
               || upper.EndsWith("KEY");
    }

    /// <summary>Adds the values of every environment variable whose name looks secret.</summary>
    public static void AddFromEnvironment()
    {
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && IsSecretName(name)
                && !ControlVariables.Contains(name, StringComparer.Ordinal))
            {
                Add(entry.Value as string);
            }
        }
    }

    /// <summary>
    /// Environment variable a parent build sets: names (one per line) whose values the child registers as secrets.
    /// It is named REDACT_ENV_NAMES, not SECRET_*, so the name does not look secret itself.
    /// </summary>
    public const string SecretKeysEnvVar = "ANDO_REDACT_ENV_NAMES";

    // ANDO's own control variables. Their values are names and lists, not secrets, and registering them
    // would make the child print "[REDACTED]" for every variable name. Kept explicit: users may name
    // real secrets ANDO_*.
    private static readonly string[] ControlVariables =
        [SecretKeysEnvVar, "ANDO_LOADED_ENV_KEYS", "ANDO_HELD_LOCKS"];

    /// <summary>Adds the values of the variables named in <see cref="SecretKeysEnvVar"/>.</summary>
    public static void AddFromListedKeys()
    {
        var list = Environment.GetEnvironmentVariable(SecretKeysEnvVar);
        if (string.IsNullOrWhiteSpace(list)) return;
        foreach (var name in list.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Add(Environment.GetEnvironmentVariable(name));
        }
    }

    /// <summary>Replaces every secret value in <paramref name="text"/> with [REDACTED].</summary>
    public static string Redact(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        string[] snapshot;
        lock (Lock)
        {
            if (Values.Count == 0) return text;
            // Longest first, so a secret containing another is redacted whole.
            snapshot = Values.OrderByDescending(v => v.Length).ToArray();
        }

        foreach (var secret in snapshot)
        {
            text = text.Replace(secret, "[REDACTED]");
        }
        return text;
    }

    /// <summary>Clears all values (tests only).</summary>
    internal static void Clear()
    {
        lock (Lock)
        {
            Values.Clear();
        }
    }
}
