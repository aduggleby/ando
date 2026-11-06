// =============================================================================
// GitOperations.cs
//
// Summary: Provides git CLI operations for build scripts.
//
// GitOperations exposes git commands (tag, push) as typed methods for build
// scripts. These operations typically run at the end of a release workflow
// after successful builds.
//
// Example usage in build.csando:
//   var release = DefineProfile("release");
//   if (release) {
//       Git.Tag("v1.0.0");
//       Git.Push();
//       Git.PushTags();
//   }
//
// Design Decisions:
// - Operations are separate (Tag, Push, PushTags) for maximum flexibility
// - Runs on host (not container) since git credentials are on host
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;
using Ando.Workflow;

namespace Ando.Operations;

/// <summary>
/// Git CLI operations for version control.
/// Methods register steps in the workflow rather than executing immediately.
/// Git commands always run on the HOST (not in containers) since .git lives on host.
/// </summary>
public class GitOperations : OperationsBase
{
    // Host executor for git commands - git must run on host where .git directory exists.
    private readonly ICommandExecutor _hostExecutor;

    // Per-run state; SourceRoot is where git runs (the --ref worktree or the project).
    private readonly RunSession? _session;

    public GitOperations(
        StepRegistry registry,
        IBuildLogger logger,
        Func<ICommandExecutor> executorFactory,
        RunSession? session = null)
        : base(registry, logger, executorFactory)
    {
        // Always use ProcessRunner for git - it runs on host, not in container.
        _hostExecutor = new HostGitExecutor(new Execution.ProcessRunner(logger), () => _session?.SourceRoot);
        _session = session;
    }

    // Runs git on the host in the source root (the --ref worktree when set).
    private sealed class HostGitExecutor(ICommandExecutor inner, Func<string?> sourceRoot) : ICommandExecutor
    {
        public Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null)
        {
            options ??= new CommandOptions();
            options.WorkingDirectory ??= sourceRoot();
            return inner.ExecuteAsync(command, args, options);
        }

        public bool IsAvailable(string command) => inner.IsAvailable(command);
    }

    // Registers a git command that runs on the host (not in container).
    private void RegisterHostCommand(string stepName, Func<ArgumentBuilder> buildArgs, string? context = null)
    {
        Registry.Register(stepName, async () =>
        {
            var args = buildArgs().Build();
            var result = await _hostExecutor.ExecuteAsync("git", args);
            return result.Success;
        }, context);
    }

    // =========================================================================
    // Evaluation-time git queries. These run on the host while the script is
    // evaluated, so their values can be used directly in the script.
    // =========================================================================

    /// <summary>
    /// Full SHA of the commit being built (HEAD, or the commit given with --ref).
    /// Usage: var commit = Git.Commit;
    /// </summary>
    public string Commit => _commit ??= RunGit("rev-parse", "HEAD");
    private string? _commit;

    /// <summary>
    /// Short SHA (7+ characters) of the commit being built.
    /// Usage: Log.Info($"Building {Git.ShortCommit}");
    /// </summary>
    public string ShortCommit => _shortCommit ??= RunGit("rev-parse", "--short", "HEAD");
    private string? _shortCommit;

    /// <summary>
    /// Fails the build before any step runs when tracked files are modified,
    /// staged or deleted, or untracked files that are not ignored exist, under
    /// <paramref name="paths"/> (or anywhere in the repository when none are given).
    /// ANDO's own build.csando.log and .ando/ are ignored.
    /// With --ref the build uses a clean checkout, so this always passes.
    /// Usage: Git.RequireClean("src", "web", "infra", "Directory.Build.props");
    /// </summary>
    public void RequireClean(params string[] paths)
    {
        // ANDO's own files (the build log it creates in the working directory
        // before evaluation, and .ando/) never count as uncommitted changes.
        var args = new List<string> { "status", "--porcelain", "--untracked-files=all", "--" };
        args.AddRange(paths.Length > 0 ? paths : [":/"]);
        args.AddRange([":(exclude)build.csando.log", ":(exclude).ando"]);

        var status = RunGit([.. args]);
        if (string.IsNullOrWhiteSpace(status))
        {
            Logger.Info(paths.Length > 0
                ? $"Git: working tree is clean ({string.Join(", ", paths)})"
                : "Git: working tree is clean");
            return;
        }

        var lines = status.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        var shown = string.Join("\n", lines.Take(20).Select(l => $"  {l}"));
        var more = lines.Length > 20 ? $"\n  ... and {lines.Length - 20} more" : "";
        throw new InvalidOperationException(
            $"The working tree has uncommitted changes{(paths.Length > 0 ? $" under {string.Join(", ", paths)}" : "")}:\n" +
            $"{shown}{more}\nCommit or stash them, or build a commit with `ando run --ref <commit>`.");
    }

    /// <summary>
    /// Whether any file under <paramref name="paths"/> differs between
    /// <paramref name="sinceRef"/> and the commit being built (committed changes
    /// only). With no paths, checks the whole repository.
    /// Usage: if (Git.ChangedSince(Git.LastTag("v*"), "src/Worker", "services/signing")) { ... }
    /// </summary>
    public bool ChangedSince(string sinceRef, params string[] paths)
    {
        var args = new List<string> { "diff", "--name-only", $"{sinceRef}..HEAD" };
        if (paths.Length > 0)
        {
            args.Add("--");
            args.AddRange(paths);
        }
        return !string.IsNullOrWhiteSpace(RunGit([.. args]));
    }

    /// <summary>
    /// The most recent tag reachable from the commit being built that matches
    /// <paramref name="pattern"/> (glob, e.g. "v*"), or null when there is none.
    /// Usage: var last = Git.LastTag("v*");
    /// </summary>
    public string? LastTag(string pattern = "*")
    {
        var (exitCode, output, _) = RunGitRaw("describe", "--tags", "--abbrev=0", "--match", pattern, "HEAD");
        return exitCode == 0 && !string.IsNullOrWhiteSpace(output) ? output : null;
    }

    private string RunGit(params string[] args)
    {
        var (exitCode, output, error) = RunGitRaw(args);
        if (exitCode != 0)
        {
            throw new InvalidOperationException($"git {string.Join(' ', args)} failed: {error}".Trim());
        }
        return output;
    }

    private (int ExitCode, string Output, string Error) RunGitRaw(params string[] args)
    {
        var startInfo = new System.Diagnostics.ProcessStartInfo("git")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = _session?.SourceRoot ?? Environment.CurrentDirectory
        };
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = System.Diagnostics.Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start git.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        return (process.ExitCode, stdout.Result.Trim(), stderr.Result.Trim());
    }

    /// <summary>
    /// Creates a git tag with the specified name.
    /// </summary>
    /// <param name="tagName">The tag name (e.g., "v1.0.0").</param>
    /// <param name="configure">Optional configuration for the tag.</param>
    public void Tag(string tagName, Action<GitTagOptions>? configure = null)
    {
        var options = new GitTagOptions();
        configure?.Invoke(options);

        Registry.Register("Git.Tag", async () =>
        {
            // Check if tag already exists when SkipIfExists is enabled
            if (options.SkipIfExists)
            {
                var checkResult = await _hostExecutor.ExecuteAsync("git", ["tag", "-l", tagName]);
                if (checkResult.Success && !string.IsNullOrWhiteSpace(checkResult.Output))
                {
                    Logger.Warning($"Tag '{tagName}' already exists - skipped");
                    return true;
                }
            }

            var args = new ArgumentBuilder()
                .Add("tag")
                .AddFlag(options.Annotated, "-a")
                .Add(tagName)
                .AddIf(options.Annotated, "-m", options.Message ?? tagName)
                .Build();

            var result = await _hostExecutor.ExecuteAsync("git", args);
            return result.Success;
        }, tagName);
    }

    /// <summary>
    /// Pushes the current branch to the remote.
    /// </summary>
    /// <param name="configure">Optional configuration for the push.</param>
    public void Push(Action<GitPushOptions>? configure = null)
    {
        var options = new GitPushOptions();
        configure?.Invoke(options);

        RegisterHostCommand("Git.Push",
            () => new ArgumentBuilder()
                .Add("push")
                .AddIfNotNull(options.Remote)
                .AddIfNotNull(options.Branch)
                .AddFlag(options.SetUpstream, "-u")
                .AddFlag(options.Force, "--force"));
    }

    /// <summary>
    /// Pushes all tags to the remote.
    /// </summary>
    /// <param name="remote">The remote to push to (default: origin).</param>
    public void PushTags(string remote = "origin")
    {
        Registry.Register("Git.PushTags", async () =>
        {
            // Resolve local tags first.
            var localTagsResult = await _hostExecutor.ExecuteAsync("git", ["tag", "-l"]);
            if (!localTagsResult.Success)
            {
                return false;
            }

            var localTags = (localTagsResult.Output ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToList();

            if (localTags.Count == 0)
            {
                Logger.Info("No local tags found to push");
                return true;
            }

            // Query remote tags and push only tags that don't already exist remotely.
            var remoteTagsResult = await _hostExecutor.ExecuteAsync("git", ["ls-remote", "--tags", "--refs", remote]);
            if (!remoteTagsResult.Success)
            {
                return false;
            }

            var remoteTags = (remoteTagsResult.Output ?? string.Empty)
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(line => line.Split('\t', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                .Where(parts => parts.Length == 2 && parts[1].StartsWith("refs/tags/", StringComparison.Ordinal))
                .Select(parts => parts[1]["refs/tags/".Length..])
                .ToHashSet(StringComparer.Ordinal);

            var tagsToPush = localTags
                .Where(tag => !remoteTags.Contains(tag))
                .ToList();

            if (tagsToPush.Count == 0)
            {
                Logger.Info($"All {localTags.Count} tag(s) already exist on {remote} - skipping tag push");
                return true;
            }

            foreach (var tag in tagsToPush)
            {
                var pushResult = await _hostExecutor.ExecuteAsync("git", ["push", remote, $"refs/tags/{tag}"]);
                if (!pushResult.Success)
                {
                    return false;
                }
            }

            Logger.Info($"Pushed {tagsToPush.Count} new tag(s) to {remote}");
            return true;
        }, remote);
    }

}

/// <summary>Options for 'git tag' command.</summary>
public class GitTagOptions
{
    /// <summary>Create an annotated tag (recommended for releases).</summary>
    public bool Annotated { get; set; } = true;

    /// <summary>Message for annotated tag. Defaults to tag name if not set.</summary>
    public string? Message { get; set; }

    /// <summary>Skip creating the tag if it already exists (instead of failing).</summary>
    public bool SkipIfExists { get; set; }

    /// <summary>Sets the tag message.</summary>
    public GitTagOptions WithMessage(string message)
    {
        Message = message;
        return this;
    }

    /// <summary>Creates a lightweight tag instead of annotated.</summary>
    public GitTagOptions AsLightweight()
    {
        Annotated = false;
        return this;
    }

    /// <summary>Skip creating the tag if it already exists (shows warning instead of failing).</summary>
    public GitTagOptions WithSkipIfExists()
    {
        SkipIfExists = true;
        return this;
    }
}

/// <summary>Options for 'git push' command.</summary>
public class GitPushOptions
{
    /// <summary>Remote to push to.</summary>
    public string? Remote { get; set; }

    /// <summary>Branch to push.</summary>
    public string? Branch { get; set; }

    /// <summary>Set upstream tracking reference.</summary>
    public bool SetUpstream { get; set; }

    /// <summary>Force push (use with caution).</summary>
    public bool Force { get; set; }

    /// <summary>Sets the remote to push to.</summary>
    public GitPushOptions ToRemote(string remote)
    {
        Remote = remote;
        return this;
    }

    /// <summary>Sets the branch to push.</summary>
    public GitPushOptions ToBranch(string branch)
    {
        Branch = branch;
        return this;
    }

    /// <summary>Sets up upstream tracking.</summary>
    public GitPushOptions WithUpstream()
    {
        SetUpstream = true;
        return this;
    }
}
