// =============================================================================
// HookRunner.cs
//
// Summary: Discovers and executes hook scripts for ANDO commands.
//
// HookRunner searches for hook scripts in predefined locations and executes
// them before and after ANDO commands. Hooks are .csando files that run on
// the host machine using the same Roslyn scripting as build.csando.
//
// Hook Types:
// - ando-pre.csando: Runs before ANY command
// - ando-pre-{cmd}.csando: Runs before specific command
// - ando-post-{cmd}.csando: Runs after specific command
// - ando-post.csando: Runs after ANY command
//
// Search Locations (first found wins):
// 1. ./scripts/ando-{hook}.csando
// 2. ./ando-{hook}.csando
// A root file shadowed by the scripts/ file of the same name never runs; a
// warning says so. `ando hook check` uses the same discovery (Discover).
//
// Design Decisions:
// - Pre-hooks can abort the command by returning non-zero or throwing
// - Post-hooks only warn on failure (command already completed)
// - Missing hooks are silently skipped (no error or warning)
// - General hooks run before command-specific hooks
// =============================================================================

using Ando.Logging;

namespace Ando.Hooks;

/// <summary>
/// Discovers and executes hook scripts for ANDO commands.
/// </summary>
public class HookRunner
{
    private readonly string _projectRoot;
    private readonly HookScriptHost _scriptHost;
    private readonly IBuildLogger _logger;
    private const int HookTimeoutMs = 300000; // 5 minutes

    public enum HookType { Pre, Post }

    public HookRunner(string projectRoot, IBuildLogger logger)
    {
        _projectRoot = projectRoot;
        _scriptHost = new HookScriptHost(projectRoot, logger);
        _logger = logger;
    }

    /// <summary>
    /// Runs hooks for the specified command and type.
    /// </summary>
    /// <param name="type">Pre or Post hook type.</param>
    /// <param name="command">Command name (bump, commit, etc.).</param>
    /// <param name="context">Context with environment variables for hooks.</param>
    /// <returns>True if all hooks succeeded, false if a pre-hook failed.</returns>
    public async Task<bool> RunHooksAsync(HookType type, string command, HookContext context)
    {
        var typeName = type.ToString().ToLower();

        // Run general hook first (ando-pre or ando-post).
        var generalHook = FindHook($"ando-{typeName}");
        if (generalHook != null)
        {
            if (!await ExecuteHookAsync(generalHook, context, isPreHook: type == HookType.Pre))
                return false;
        }

        // Run command-specific hook (ando-pre-bump, ando-post-commit, etc.).
        var specificHook = FindHook($"ando-{typeName}-{command}");
        if (specificHook != null)
        {
            if (!await ExecuteHookAsync(specificHook, context, isPreHook: type == HookType.Pre))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Finds a hook script in the search locations.
    /// </summary>
    /// <param name="hookName">Hook name without extension (e.g., "ando-pre-bump").</param>
    /// <returns>Full path to hook script, or null if not found.</returns>
    private string? FindHook(string hookName)
    {
        var location = Locate(_projectRoot, hookName);
        if (location?.ShadowedPath != null)
        {
            _logger.Warning(ShadowWarning(location));
        }
        return location?.Path;
    }

    /// <summary>
    /// A hook script HookRunner would run, and the root file of the same name it
    /// ignores because scripts/ takes precedence (null when there is none).
    /// </summary>
    /// <param name="Path">Full path of the hook that runs.</param>
    /// <param name="ShadowedPath">Full path of the ignored root file, or null.</param>
    public sealed record HookLocation(string Path, string? ShadowedPath);

    // Hook file names: ando-pre, ando-post, ando-pre-<cmd>, ando-post-<cmd> (.csando).
    private static readonly System.Text.RegularExpressions.Regex HookFileNamePattern =
        new(@"^ando-(pre|post)(-[a-z0-9][a-z0-9-]*)?\.csando$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    /// Whether <paramref name="path"/> has a hook script file name
    /// (ando-pre.csando, ando-post.csando, ando-pre-&lt;cmd&gt;.csando, ando-post-&lt;cmd&gt;.csando).
    /// </summary>
    public static bool IsHookFileName(string path) =>
        HookFileNamePattern.IsMatch(System.IO.Path.GetFileName(path));

    /// <summary>
    /// Finds <paramref name="hookName"/> (without extension) in ./scripts/ first, then the project root.
    /// Returns null when neither exists.
    /// </summary>
    public static HookLocation? Locate(string projectRoot, string hookName)
    {
        var inScripts = System.IO.Path.Combine(projectRoot, "scripts", $"{hookName}.csando");
        var inRoot = System.IO.Path.Combine(projectRoot, $"{hookName}.csando");
        if (File.Exists(inScripts))
        {
            return new HookLocation(inScripts, File.Exists(inRoot) ? inRoot : null);
        }
        return File.Exists(inRoot) ? new HookLocation(inRoot, null) : null;
    }

    /// <summary>
    /// Every hook script HookRunner would run in <paramref name="projectRoot"/> (both locations),
    /// one entry per hook name, sorted by file name.
    /// </summary>
    public static IReadOnlyList<HookLocation> Discover(string projectRoot)
    {
        IEnumerable<string> HookFiles(string directory) => Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "ando-*.csando").Where(IsHookFileName)
            : [];

        return HookFiles(System.IO.Path.Combine(projectRoot, "scripts"))
            .Concat(HookFiles(projectRoot))
            .Select(path => System.IO.Path.GetFileNameWithoutExtension(path))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .Select(name => Locate(projectRoot, name)!)
            .ToList();
    }

    /// <summary>The warning logged when a root hook is shadowed by the scripts/ file of the same name.</summary>
    public static string ShadowWarning(HookLocation location) =>
        $"Hook {System.IO.Path.GetFileName(location.ShadowedPath)} in the project root is ignored: " +
        $"{location.Path} takes precedence. Delete or merge one of them.";

    /// <summary>
    /// Executes a single hook script.
    /// </summary>
    /// <param name="hookPath">Full path to the hook script.</param>
    /// <param name="context">Hook context with environment variables.</param>
    /// <param name="isPreHook">True for pre-hooks (abort on failure), false for post-hooks (warn only).</param>
    /// <returns>True if hook succeeded, false if hook failed and should abort.</returns>
    private async Task<bool> ExecuteHookAsync(string hookPath, HookContext context, bool isPreHook)
    {
        var hookName = Path.GetFileName(hookPath);

        try
        {
            _logger.Info($"Running hook: {hookName}");

            await _scriptHost.ExecuteAsync(hookPath, context.ToEnvironment(), HookTimeoutMs);

            return true;
        }
        catch (HookAbortException ex)
        {
            _logger.Error($"Hook aborted: {ex.Message}");
            return !isPreHook; // Pre-hooks abort, post-hooks just warn
        }
        catch (TimeoutException ex)
        {
            _logger.Error($"Hook timed out: {ex.Message}");
            return !isPreHook; // Pre-hooks abort on timeout, post-hooks continue
        }
        catch (Exception ex)
        {
            _logger.Error($"Hook failed: {ex.Message}");

            if (isPreHook)
            {
                return false; // Pre-hooks abort on failure
            }

            // Post-hooks just warn and continue.
            _logger.Warning("Continuing despite post-hook failure.");
            return true;
        }
    }
}
