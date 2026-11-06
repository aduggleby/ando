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
        if (string.IsNullOrEmpty(value) || value.Length < MinLength) return;
        lock (Lock)
        {
            Values.Add(value);
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
            if (entry.Key is string name && IsSecretName(name))
            {
                Add(entry.Value as string);
            }
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
