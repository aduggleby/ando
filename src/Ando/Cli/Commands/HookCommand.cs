// =============================================================================
// HookCommand.cs
//
// Summary: CLI command handler for 'ando hook check [file...]'.
//
// Hook scripts (ando-pre.csando, ando-post-<cmd>.csando, ...) use the hook API
// (HookGlobals: Root, Log, Shell, Env, Directory), not the build-script API, so
// `ando verify` and `ando run --dry-run` cannot check them. This command
// compiles hooks against HookGlobals with the options HookScriptHost uses and
// runs nothing.
//
// Design Decisions:
// - Without files it checks every hook HookRunner would run in the current
//   directory (./scripts/ first, then the root). A root file shadowed by the
//   scripts/ file of the same name is reported as ignored, not compiled,
//   because it never runs. ando-pre-*/ando-post-* files for commands that run
//   no hooks (for example ando-pre-run.csando) are warned about, not compiled
// - Exit codes: 0 all hooks compile (or none found), 1 a hook has compile
//   errors, 2 a named file does not exist or the arguments are wrong
// =============================================================================

using Ando.Hooks;
using Ando.Logging;

namespace Ando.Cli.Commands;

/// <summary>
/// CLI command handler for 'ando hook check'. Compiles hook scripts without running them.
/// </summary>
public class HookCommand(IBuildLogger logger)
{
    /// <summary>The usage line shown for wrong arguments.</summary>
    public const string Usage = "Usage: ando hook check [file...]";

    /// <summary>
    /// Runs 'ando hook &lt;subcommand&gt; [args]'.
    /// </summary>
    /// <param name="args">Arguments after 'hook'.</param>
    /// <param name="projectRoot">Directory searched for hooks when no file is named.</param>
    /// <returns>0 when every hook compiles, 1 on compile errors, 2 on usage errors or missing files.</returns>
    public async Task<int> ExecuteAsync(string[] args, string projectRoot)
    {
        if (args.Length == 0 || args[0] != "check")
        {
            logger.Error(args.Length == 0 ? "Missing subcommand." : $"Unknown hook subcommand: {args[0]}");
            logger.Error(Usage);
            return 2;
        }

        // --no-color is a global option (handled by the CLI's logger); everything else is a file.
        var files = args[1..].Where(a => a != "--no-color").ToArray();
        if (files.FirstOrDefault(f => f.StartsWith('-')) is { } option)
        {
            logger.Error($"Unknown option for hook check: {option}");
            logger.Error(Usage);
            return 2;
        }

        return files.Length == 0
            ? await CheckDiscoveredAsync(projectRoot)
            : await CheckFilesAsync(files);
    }

    // Checks every hook HookRunner would run in the project root.
    private async Task<int> CheckDiscoveredAsync(string projectRoot)
    {
        var hooks = HookRunner.Discover(projectRoot);
        foreach (var unused in HookRunner.FindUnusedHookLikeFiles(projectRoot))
        {
            logger.Warning($"{Path.GetRelativePath(projectRoot, unused)} never runs as a hook " +
                           $"(hooks run only for {string.Join(", ", HookRunner.HookCommands)}); it was not checked.");
        }

        if (hooks.Count == 0)
        {
            logger.Info($"No hook scripts found in {Path.Combine(projectRoot, "scripts")} or {projectRoot}.");
            return 0;
        }

        var failed = false;
        foreach (var hook in hooks)
        {
            failed |= !await CheckOneAsync(hook.Path, Path.GetRelativePath(projectRoot, hook.Path));
            if (hook.ShadowedPath != null)
            {
                logger.Warning(HookRunner.ShadowWarning(hook) + " It was not checked.");
            }
        }

        return failed ? 1 : 0;
    }

    // Checks the named files (absolute, or relative to the current directory).
    private async Task<int> CheckFilesAsync(string[] files)
    {
        var missing = false;
        var failed = false;
        foreach (var file in files)
        {
            var path = Path.GetFullPath(file);
            if (!File.Exists(path))
            {
                logger.Error($"Hook file not found: {file}");
                missing = true;
                continue;
            }

            if (!HookRunner.IsHookFileName(path))
            {
                logger.Warning($"{file} is not a hook file name (ando-pre.csando, ando-post.csando, ando-pre-<command>.csando, " +
                               $"ando-post-<command>.csando with command {string.Join(", ", HookRunner.HookCommands)}), so ANDO never runs it as one.");
            }

            failed |= !await CheckOneAsync(path, file);
        }

        return missing ? 2 : failed ? 1 : 0;
    }

    // Compiles one hook and prints OK or its compile errors.
    private async Task<bool> CheckOneAsync(string path, string displayName)
    {
        var errors = await HookScriptHost.CheckAsync(path);
        if (errors.Count == 0)
        {
            logger.Info($"OK    {displayName}");
            return true;
        }

        logger.Error($"FAIL  {displayName}");
        foreach (var error in errors)
        {
            logger.Error($"  {error}");
        }
        return false;
    }
}
