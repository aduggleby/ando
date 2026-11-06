// =============================================================================
// RunSession.cs
//
// Summary: Per-run state shared by operations and the CLI.
//
// RunSession holds values that belong to one `ando run` invocation rather than
// to the build script: the directory ANDO was invoked from, whether prompts are
// answered automatically (--yes), the build container, and the values the
// script reports for the run record.
//
// Design Decisions:
// - Lives on BuildOptions so operations that already receive BuildOptions can
//   reach it without new constructor parameters
// - Container details are set by the CLI after the container exists; steps
//   read them at execution time
// =============================================================================

using Ando.Execution;

namespace Ando.Workflow;

/// <summary>
/// Per-run state shared by operations and the CLI.
/// </summary>
public class RunSession
{
    /// <summary>
    /// Host directory ANDO was invoked from (the project root). With --ref the
    /// build runs from a temporary worktree, but host files the script reads,
    /// artifacts copied to the host and run records still use this directory.
    /// Null until the CLI sets it; callers fall back to the current directory.
    /// </summary>
    public string? InvocationRoot { get; set; }

    /// <summary>Resolves a host path relative to <see cref="InvocationRoot"/>.</summary>
    public string ResolveHostPath(string path) =>
        Path.GetFullPath(Path.IsPathRooted(path)
            ? path
            : Path.Combine(InvocationRoot ?? Environment.CurrentDirectory, path));

    /// <summary>
    /// Host directory of the sources being built: the project root, or the
    /// temporary worktree with --ref. Git runs here. Null means the current directory.
    /// </summary>
    public string? SourceRoot { get; set; }

    /// <summary>
    /// Whether Ando.Confirm may prompt on the console. Null (the default) means
    /// "when stdin is not redirected"; tests set false.
    /// </summary>
    public bool? InteractiveConsole { get; set; }

    /// <summary>Whether Ando.Confirm prompts are accepted automatically (--yes).</summary>
    public bool AssumeYes { get; set; }

    /// <summary>The commit being built when running with --ref (full SHA).</summary>
    public string? Ref { get; set; }

    /// <summary>Docker manager for copying files into the build container.</summary>
    public DockerManager? DockerManager { get; set; }

    /// <summary>Build container ID, set once the container exists.</summary>
    public string? ContainerId { get; set; }

    /// <summary>Values added with Ando.Report, in the order they were reported.</summary>
    public List<KeyValuePair<string, string>> Reports { get; } = [];
}
