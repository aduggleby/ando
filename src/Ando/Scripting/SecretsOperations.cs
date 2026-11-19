// =============================================================================
// SecretsOperations.cs
//
// Summary: The `Secrets` script global. Reads secrets from host files while the
// script is evaluated and registers the values for redaction.
//
// Design Decisions:
// - Values are read on the host at evaluation time, not as steps
// - Nothing is written to the process environment or ANDO_LOADED_ENV_KEYS, so
//   a secret reaches a command only when the script passes it explicitly
// - Error messages name keys and paths, never values
// - EnvFile registers the keys the script names (they are secrets by choice).
//   UserSecrets registers a value only when the script reads it, because a user
//   secrets file also holds plain config (Region, Port) that must stay readable
//   in logs; read config through Secrets only if it is secret
// - EnvFile refuses a file in a git repository that is not gitignored, and warns
//   when it is the auto-loaded .env.ando / .env (its keys are forwarded anyway)
// =============================================================================

using System.Text.Json;
using System.Xml.Linq;
using Ando.Cli;
using Ando.Logging;
using Ando.Utilities;
using Ando.Workflow;

namespace Ando.Scripting;

/// <summary>Secret sources for build scripts.</summary>
public class SecretsOperations(RunSession? session = null, string? userSecretsRoot = null, IBuildLogger? logger = null)
{
    private string ResolveHostPath(string path) =>
        session?.ResolveHostPath(path)
        ?? Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(Environment.CurrentDirectory, path));

    /// <summary>
    /// Reads the .NET user secrets store (secrets.json) on the host, without calling dotnet.
    /// Pass the UserSecretsId, or a .csproj (or a folder holding exactly one) that has a
    /// UserSecretsId; relative paths resolve against the invocation root. Nested objects are
    /// flattened with ':' (Deploy:Azure:Secret), like the configuration system. A value is registered
    /// for redaction when the script reads it (indexer or TryGet), so read config through the set only
    /// if it is secret: a plain value such as Region is not redacted unless it was read this way.
    /// Usage: var sp = Secrets.UserSecrets("simplesign365-dev"); sp["Deploy:Azure:ClientSecret"]
    /// </summary>
    public SecretSet UserSecrets(string userSecretsIdOrProjectPath)
    {
        var id = LooksLikeProjectPath(userSecretsIdOrProjectPath)
            ? ReadUserSecretsId(ResolveHostPath(userSecretsIdOrProjectPath))
            : userSecretsIdOrProjectPath;

        var file = Path.Combine(userSecretsRoot ?? UserSecretsRoot(), id, "secrets.json");
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"User secrets file not found: {file}");
        }

        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using (var doc = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }))
        {
            Flatten(doc.RootElement, "", values);
        }

        return new SecretSet($"user secrets '{id}'", values, registerOnLoad: false);
    }

    /// <summary>
    /// Reads the named keys from a dotenv file on the host (same format as .env.ando; relative paths
    /// resolve against the invocation root). Values are registered for redaction but are NOT set by
    /// this call in the process environment and NOT forwarded to container commands: pass them to the
    /// operation that needs them. Use a separate file such as .env.secrets: .env.ando (or .env) is still
    /// loaded and forwarded to every container command by the normal env loading unless the run uses
    /// --no-env, and a warning is logged when this reads that file. A file inside a git repository
    /// that is not gitignored is refused. A missing key fails at evaluation.
    /// Usage: var cf = Secrets.EnvFile(".env.secrets", "CLOUDFLARE_API_TOKEN"); cf["CLOUDFLARE_API_TOKEN"]
    /// </summary>
    public SecretSet EnvFile(string path, params string[] keys)
    {
        var file = ResolveHostPath(path);
        if (!File.Exists(file))
        {
            throw new InvalidOperationException($"Env file not found: {file}");
        }

        var root = session?.InvocationRoot ?? Environment.CurrentDirectory;
        var autoLoaded = AndoCli.ResolveEnvFilePath(root);
        if (autoLoaded != null && Path.GetFullPath(autoLoaded) == file && session?.NoEnv != true)
        {
            logger?.Warning(
                $"Secrets.EnvFile reads {Path.GetFileName(file)}, which ando loads as the env file: its keys are forwarded to container commands anyway. " +
                "Use a separate file such as .env.secrets, or run with --no-env.");
        }

        // Exit code 1 = inside a git repository and not ignored (128 = not a repository).
        if (GitIgnoreCheck.CheckIgnore(Path.GetDirectoryName(file) ?? root, file) == 1)
        {
            throw new InvalidOperationException(
                $"Env file {file} is in a git repository but is not gitignored. Add it to .gitignore before reading secrets from it.");
        }

        var all = EnvFileParser.Parse(file);
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!all.TryGetValue(key, out var value))
            {
                throw new InvalidOperationException($"Key '{key}' is not set in {file}.");
            }
            values[key] = value;
        }

        return new SecretSet($"env file '{path}'", values, registerOnLoad: true);
    }

    private bool LooksLikeProjectPath(string value) =>
        value.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
        || value.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase)
        || value.Contains('/') || value.Contains('\\') || Directory.Exists(ResolveHostPath(value));

    private static string UserSecretsRoot() => OperatingSystem.IsWindows()
        ? Path.Combine(Environment.GetEnvironmentVariable("APPDATA") ?? "", "Microsoft", "UserSecrets")
        : Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".microsoft", "usersecrets");

    private static string ReadUserSecretsId(string path)
    {
        var project = path;
        if (Directory.Exists(path))
        {
            var projects = Directory.GetFiles(path, "*.csproj");
            if (projects.Length != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one .csproj in '{path}' to read the UserSecretsId, found {projects.Length}.");
            }
            project = projects[0];
        }
        if (!File.Exists(project))
        {
            throw new InvalidOperationException($"Project not found: {project}");
        }

        var id = XDocument.Load(project).Descendants("UserSecretsId").Select(e => e.Value.Trim()).FirstOrDefault(v => v.Length > 0);
        return id ?? throw new InvalidOperationException($"No <UserSecretsId> in {project}.");
    }

    private static void Flatten(JsonElement element, string prefix, Dictionary<string, string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    Flatten(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}:{property.Name}", into);
                }
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    Flatten(item, $"{prefix}:{index++}", into);
                }
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                break;
            default:
                into[prefix] = element.ValueKind is JsonValueKind.String ? element.GetString()! : element.GetRawText();
                break;
        }
    }
}

/// <summary>
/// A read-only set of secret values. Reading a missing key throws an error that names the key,
/// never a value. Values are registered for redaction when the set is created (env file keys the script
/// named) or when the script reads them (user secrets).
/// </summary>
public sealed class SecretSet
{
    private readonly string _source;
    private readonly IReadOnlyDictionary<string, string> _values;

    internal SecretSet(string source, IReadOnlyDictionary<string, string> values, bool registerOnLoad)
    {
        _source = source;
        _values = values;
        if (registerOnLoad)
        {
            foreach (var value in values.Values)
            {
                SecretValues.Add(value);
            }
        }
    }

    /// <summary>The value of <paramref name="key"/>; throws when the key is missing.</summary>
    public string this[string key]
    {
        get
        {
            if (!_values.TryGetValue(key, out var value))
            {
                throw new InvalidOperationException($"Key '{key}' was not found in {_source}.");
            }
            SecretValues.Add(value);
            return value;
        }
    }

    /// <summary>Gets the value of <paramref name="key"/> when present.</summary>
    public bool TryGet(string key, out string? value)
    {
        var found = _values.TryGetValue(key, out var v);
        SecretValues.Add(v);
        value = v;
        return found;
    }

    /// <inheritdoc />
    public override string ToString() => $"SecretSet({_source}, {_values.Count} keys)";
}
