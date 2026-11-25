// =============================================================================
// DryRunEnvironment.cs
//
// Summary: Records required environment variables that are missing during a
// `ando run --dry-run` and hands out placeholders instead of failing.
//
// A dry run evaluates the script on the host to print the step tree. Scripts
// often read secrets while they load (Env("X"), Cloudflare.EnsureAuthenticated,
// Azure.LoginWithServicePrincipal), so a dry run without the secrets used to
// fail before any tree was printed. During a dry run those reads return a
// placeholder such as "<CLOUDFLARE_API_TOKEN>" and the name is recorded; the
// CLI prints one warning listing the names after the tree.
//
// Design Decisions:
// - One instance per run, held by RunSession and published to the script's
//   async flow through an AsyncLocal while the script is evaluated. Static
//   helpers (EnvironmentHelper, ScriptGlobals.Env) read Current, and parallel
//   tests never see each other's state.
// - Current is null outside a dry run, so real runs take the old code path.
// - Only names are recorded, never values. Placeholders are never written to
//   the process environment or to the secret redaction list.
// =============================================================================

namespace Ando.Utilities;

/// <summary>
/// Per-run record of required environment variables that were missing during a dry run.
/// </summary>
public sealed class DryRunEnvironment
{
    private static readonly AsyncLocal<DryRunEnvironment?> CurrentValue = new();

    private readonly object _lock = new();
    private readonly List<string> _missing = [];

    /// <summary>
    /// The tracker of the dry run whose script is being evaluated in this async flow,
    /// or null (real runs, hooks, everything outside script evaluation).
    /// </summary>
    public static DryRunEnvironment? Current => CurrentValue.Value;

    /// <summary>Names of the missing variables, deduplicated, in the order they were first read.</summary>
    public IReadOnlyList<string> MissingNames
    {
        get
        {
            lock (_lock)
            {
                return _missing.ToArray();
            }
        }
    }

    /// <summary>The placeholder returned for a missing variable, for example "&lt;API_KEY&gt;".</summary>
    public static string Placeholder(string name) => $"<{name}>";

    /// <summary>Records <paramref name="name"/> as missing and returns its placeholder.</summary>
    public string RecordMissing(string name)
    {
        lock (_lock)
        {
            if (!_missing.Contains(name))
            {
                _missing.Add(name);
            }
        }
        return Placeholder(name);
    }

    /// <summary>
    /// Makes <paramref name="tracker"/> the <see cref="Current"/> tracker for this async flow
    /// until the returned scope is disposed. A null tracker clears it.
    /// </summary>
    public static IDisposable Activate(DryRunEnvironment? tracker)
    {
        var previous = CurrentValue.Value;
        CurrentValue.Value = tracker;
        return new Scope(previous);
    }

    /// <summary>
    /// The single warning printed after a dry run that used placeholders, or null when none were used.
    /// </summary>
    public string? FormatWarning()
    {
        var names = MissingNames;
        return names.Count == 0
            ? null
            : "Dry run: these required environment variables are not set; placeholders were used: "
              + string.Join(", ", names) + ". A real run fails without them.";
    }

    private sealed class Scope(DryRunEnvironment? previous) : IDisposable
    {
        public void Dispose() => CurrentValue.Value = previous;
    }
}
