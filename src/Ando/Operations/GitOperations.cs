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
/// </summary>
public class GitOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Creates a git tag with the specified name.
    /// </summary>
    /// <param name="tagName">The tag name (e.g., "v1.0.0").</param>
    /// <param name="configure">Optional configuration for the tag.</param>
    public void Tag(string tagName, Action<GitTagOptions>? configure = null)
    {
        var options = new GitTagOptions();
        configure?.Invoke(options);

        RegisterCommand("Git.Tag", "git",
            () => new ArgumentBuilder()
                .Add("tag")
                .AddFlag(options.Annotated, "-a")
                .Add(tagName)
                .AddIf(options.Annotated, "-m", options.Message ?? tagName),
            tagName);
    }

    /// <summary>
    /// Pushes the current branch to the remote.
    /// </summary>
    /// <param name="configure">Optional configuration for the push.</param>
    public void Push(Action<GitPushOptions>? configure = null)
    {
        var options = new GitPushOptions();
        configure?.Invoke(options);

        RegisterCommand("Git.Push", "git",
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
        RegisterCommand("Git.PushTags", "git",
            () => new ArgumentBuilder()
                .Add("push", remote, "--tags"),
            remote);
    }

    /// <summary>
    /// Adds files to the staging area.
    /// </summary>
    /// <param name="paths">Paths to add (default: "." for all).</param>
    public void Add(params string[] paths)
    {
        var pathsToAdd = paths.Length > 0 ? paths : ["."];

        RegisterCommand("Git.Add", "git",
            () => new ArgumentBuilder()
                .Add("add")
                .Add(pathsToAdd),
            string.Join(", ", pathsToAdd));
    }

    /// <summary>
    /// Commits staged changes.
    /// </summary>
    /// <param name="message">The commit message.</param>
    /// <param name="configure">Optional configuration for the commit.</param>
    public void Commit(string message, Action<GitCommitOptions>? configure = null)
    {
        var options = new GitCommitOptions();
        configure?.Invoke(options);

        RegisterCommand("Git.Commit", "git",
            () => new ArgumentBuilder()
                .Add("commit", "-m", message)
                .AddFlag(options.AllowEmpty, "--allow-empty"));
    }
}

/// <summary>Options for 'git tag' command.</summary>
public class GitTagOptions
{
    /// <summary>Create an annotated tag (recommended for releases).</summary>
    public bool Annotated { get; set; } = true;

    /// <summary>Message for annotated tag. Defaults to tag name if not set.</summary>
    public string? Message { get; set; }

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

/// <summary>Options for 'git commit' command.</summary>
public class GitCommitOptions
{
    /// <summary>Allow creating a commit with no changes.</summary>
    public bool AllowEmpty { get; set; }

    /// <summary>Allows creating an empty commit.</summary>
    public GitCommitOptions WithAllowEmpty()
    {
        AllowEmpty = true;
        return this;
    }
}
