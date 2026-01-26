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

    public GitOperations(
        StepRegistry registry,
        IBuildLogger logger,
        Func<ICommandExecutor> executorFactory)
        : base(registry, logger, executorFactory)
    {
        // Always use ProcessRunner for git - it runs on host, not in container.
        _hostExecutor = new Execution.ProcessRunner(logger);
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
        RegisterHostCommand("Git.PushTags",
            () => new ArgumentBuilder()
                .Add("push", remote, "--tags"),
            remote);
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

