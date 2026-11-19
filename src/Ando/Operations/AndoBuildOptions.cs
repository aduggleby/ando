// =============================================================================
// AndoBuildOptions.cs
//
// Summary: Fluent builder for configuring nested build options.
//
// AndoBuildOptions uses a fluent builder pattern for readable configuration
// of child build execution. Each method returns 'this' to allow chaining.
//
// Example usage:
//   Ando.Build(Directory("./website"), o => o
//       .WithVerbosity("detailed")
//       .WithEnv("CLOUDFLARE_API_TOKEN", token.Value)
//       .ColdStart());
//
// Design Decisions:
// - Fluent builder pattern for readable, chainable configuration
// - Options map to ando CLI flags
// - ColdStart forces fresh container (useful for debugging)
// =============================================================================

using Ando.Logging;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// Fluent builder for configuring nested build options.
/// Methods return 'this' for chaining.
/// </summary>
public class AndoBuildOptions
{
    /// <summary>Verbosity level (quiet, minimal, normal, detailed).</summary>
    public string? Verbosity { get; private set; }

    /// <summary>Force fresh container instead of reusing warm container.</summary>
    public bool Cold { get; private set; }

    /// <summary>Enable Docker-in-Docker for the child build.</summary>
    public bool Dind { get; private set; }

    /// <summary>Custom Docker image for the child build.</summary>
    public string? Image { get; private set; }

    /// <summary>Profiles to activate for the child build (comma-separated).</summary>
    public string? Profile { get; private set; }

    /// <summary>Sets the verbosity level for the child build.</summary>
    public AndoBuildOptions WithVerbosity(string verbosity)
    {
        Verbosity = verbosity;
        return this;
    }

    /// <summary>Forces a fresh container for the child build.</summary>
    public AndoBuildOptions ColdStart(bool cold = true)
    {
        Cold = cold;
        return this;
    }

    /// <summary>Enables Docker-in-Docker for the child build.</summary>
    public AndoBuildOptions WithDind(bool dind = true)
    {
        Dind = dind;
        return this;
    }

    /// <summary>Sets a custom Docker image for the child build.</summary>
    public AndoBuildOptions WithImage(string image)
    {
        Image = image;
        return this;
    }

    /// <summary>Sets profiles to activate for the child build.</summary>
    public AndoBuildOptions WithProfile(string profile)
    {
        Profile = profile;
        return this;
    }

    private readonly Dictionary<string, string> _env = new(StringComparer.Ordinal);
    private readonly HashSet<string> _secretKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Sets an environment variable for the child build only (never in this process).
    /// When used, the child forwards exactly these variables into its containers
    /// (its ANDO_LOADED_ENV_KEYS is reset to them) instead of everything loaded
    /// from the env file. The value is redacted in both builds when the name looks
    /// secret (TOKEN, PASSWORD, ...) or <paramref name="secret"/> is true.
    /// </summary>
    public AndoBuildOptions WithEnv(string name, string value, bool secret = false)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('=') || name.Contains('\n'))
        {
            throw new ArgumentException("Environment variable name must be non-empty and contain no '=' or newline.", nameof(name));
        }
        ArgumentNullException.ThrowIfNull(value);

        _env[name] = value;
        if (secret || SecretValues.IsSecretName(name))
        {
            SecretValues.Add(value);
            _secretKeys.Add(name);
        }
        return this;
    }

    /// <summary>
    /// The variables to set in the child process: the WithEnv values, plus (only when WithEnv
    /// was used) the child's tracked key list and the names of its secret keys.
    /// </summary>
    internal IReadOnlyDictionary<string, string> ChildEnvironment()
    {
        var env = new Dictionary<string, string>(_env, StringComparer.Ordinal);
        if (_env.Count > 0)
        {
            env[LoadedEnvironmentVariables.TrackedKeysEnvVar] = string.Join('\n', _env.Keys.Order(StringComparer.Ordinal));
        }
        // Merge with the list this process inherited, so grandchildren keep redacting what the parent marked.
        var names = new HashSet<string>(_secretKeys, StringComparer.Ordinal);
        var inherited = Environment.GetEnvironmentVariable(SecretValues.SecretKeysEnvVar);
        if (!string.IsNullOrWhiteSpace(inherited))
        {
            names.UnionWith(inherited.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        if (names.Count > 0 && (_secretKeys.Count > 0 || _env.Count > 0))
        {
            env[SecretValues.SecretKeysEnvVar] = string.Join('\n', names.Order(StringComparer.Ordinal));
        }
        return env;
    }

    /// <summary>Set from the parent run: the commit the parent builds (--ref); the child builds the same commit.</summary>
    internal string? Ref { get; set; }

    /// <summary>Set from the parent run: it used --yes, so the child accepts Ando.Confirm prompts too.</summary>
    internal bool AssumeYes { get; set; }

    /// <summary>Where the child writes its run record path (--record-path-file).</summary>
    internal string? RecordPathFile { get; set; }

    /// <summary>Set from the parent run: it used --no-env, so the child skips the env file too.</summary>
    internal bool NoEnv { get; set; }

    /// <summary>Builds the CLI arguments for ando run.</summary>
    internal string[] BuildArgs()
    {
        var args = new List<string> { "run" };

        if (NoEnv)
        {
            args.Add("--no-env");
        }

        if (AssumeYes)
        {
            args.Add("--yes");
        }

        if (!string.IsNullOrEmpty(Ref))
        {
            args.Add("--ref");
            args.Add(Ref);
        }

        if (!string.IsNullOrEmpty(RecordPathFile))
        {
            args.Add("--record-path-file");
            args.Add(RecordPathFile);
        }

        if (!string.IsNullOrEmpty(Verbosity))
        {
            args.Add("--verbosity");
            args.Add(Verbosity);
        }

        if (Cold)
        {
            args.Add("--cold");
        }

        if (Dind)
        {
            args.Add("--dind");
        }

        if (!string.IsNullOrEmpty(Image))
        {
            args.Add("--image");
            args.Add(Image);
        }

        if (!string.IsNullOrEmpty(Profile))
        {
            args.Add("-p");
            args.Add(Profile);
        }

        return args.ToArray();
    }
}
