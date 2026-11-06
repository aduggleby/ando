// =============================================================================
// RefWorktree.cs
//
// Summary: A temporary, detached git worktree of the commit given with --ref.
//
// `ando run --ref <commit|tag>` builds exactly that commit: ANDO checks it out
// into a new worktree under the system temp directory, evaluates the build
// script from there and copies only those files into the container, so edits
// in the shared working tree cannot change what is built. The worktree is
// removed when the run ends.
// =============================================================================

using Ando.Execution;
using Ando.Logging;

namespace Ando.Cli;

/// <summary>
/// A temporary detached worktree for `ando run --ref`.
/// </summary>
public sealed class RefWorktree : IAsyncDisposable
{
    private readonly string _repoRoot;
    private readonly IBuildLogger _logger;

    private RefWorktree(string repoRoot, string path, string commit, string scriptPath, IBuildLogger logger)
    {
        _repoRoot = repoRoot;
        Path = path;
        Commit = commit;
        ScriptPath = scriptPath;
        _logger = logger;
    }

    /// <summary>Worktree directory.</summary>
    public string Path { get; }

    /// <summary>Full SHA of the checked-out commit.</summary>
    public string Commit { get; }

    /// <summary>The build script's path inside the worktree.</summary>
    public string ScriptPath { get; }

    /// <summary>
    /// Resolves <paramref name="gitRef"/> in the repository containing
    /// <paramref name="scriptPath"/> and checks it out into a temporary worktree.
    /// </summary>
    public static async Task<RefWorktree> CreateAsync(string scriptPath, string gitRef, IBuildLogger logger)
    {
        var scriptDirectory = System.IO.Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;

        var repoRoot = await GitAsync(logger, scriptDirectory, "rev-parse", "--show-toplevel")
            ?? throw new InvalidOperationException($"--ref needs a git repository, but {scriptDirectory} is not in one.");
        var commit = await GitAsync(logger, repoRoot, "rev-parse", "--verify", "--quiet", $"{gitRef}^{{commit}}")
            ?? throw new InvalidOperationException($"--ref {gitRef}: no such commit, tag or branch.");

        var relativeScript = System.IO.Path.GetRelativePath(repoRoot, scriptPath).Replace('\\', '/');
        if (await GitAsync(logger, repoRoot, "cat-file", "-e", $"{commit}:{relativeScript}") == null)
        {
            throw new InvalidOperationException(
                $"--ref {gitRef}: the build script {relativeScript} does not exist in that commit.");
        }

        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), DirectoryName(commit));
        if (await GitAsync(logger, repoRoot, "worktree", "add", "--detach", "--quiet", path, commit) == null)
        {
            throw new InvalidOperationException($"--ref {gitRef}: could not create a worktree at {path}.");
        }

        var worktreeScript = System.IO.Path.Combine(path, relativeScript);
        return new RefWorktree(repoRoot, path, commit, worktreeScript, logger);
    }

    /// <summary>A short, unique worktree directory name for a commit.</summary>
    internal static string DirectoryName(string commit) => $"ando-ref-{commit[..12]}-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>Removes the worktree.</summary>
    public async ValueTask DisposeAsync()
    {
        if (await GitAsync(_logger, _repoRoot, "worktree", "remove", "--force", Path) == null)
        {
            _logger.Warning($"Could not remove the --ref worktree {Path}. Remove it with: git worktree remove --force {Path}");
        }
    }

    // Runs git and returns trimmed stdout, or null when git fails.
    private static async Task<string?> GitAsync(IBuildLogger logger, string workingDirectory, params string[] args)
    {
        var runner = new ProcessRunner(logger);
        var result = await runner.ExecuteAsync("git", args, new CommandOptions
        {
            WorkingDirectory = workingDirectory,
            SuppressOutput = true,
            TimeoutMs = 120_000
        });
        return result.Success ? (result.Output ?? "").Trim() : null;
    }
}
