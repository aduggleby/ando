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
        var searchPaths = new[]
        {
            Path.Combine(_projectRoot, "scripts", $"{hookName}.csando"),
            Path.Combine(_projectRoot, $"{hookName}.csando")
        };

        return searchPaths.FirstOrDefault(File.Exists);
    }

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
