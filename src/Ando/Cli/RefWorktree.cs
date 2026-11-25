// =============================================================================
// RefWorktree.cs
//
// Summary: A temporary, detached git worktree of the commit given with --ref.
//
// `ando run --ref <commit|tag>` builds exactly that commit: ANDO checks it out
// into a new worktree in the host scratch folder (~/.cache/ando/tmp, or
// ANDO_TMPDIR, see HostScratch), evaluates the build script from there and copies only those
// files into the container, so edits in the shared working tree cannot change
// what is built. The worktree is removed when the run ends.
//
// Each worktree has a sibling lock file (<name>.lock) that the run holds while
// it lives. A run that was killed leaves its worktree behind; the next run
// removes every worktree whose lock it can take. Entries without a lock file are
// never touched, and a system temp folder (/tmp and the like) is never swept.
// =============================================================================

using Ando.Execution;
using Ando.Logging;

namespace Ando.Cli;

/// <summary>
/// A temporary detached worktree for `ando run --ref`.
/// </summary>
public sealed class RefWorktree : IAsyncDisposable
{
    private const string Prefix = "ando-ref-";

    private readonly string _repoRoot;
    private readonly IBuildLogger _logger;
    private readonly FileStream? _lock;

    private RefWorktree(string repoRoot, string path, string commit, string scriptPath, FileStream? lockStream, IBuildLogger logger)
    {
        _repoRoot = repoRoot;
        Path = path;
        Commit = commit;
        ScriptPath = scriptPath;
        _lock = lockStream;
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
    public static Task<RefWorktree> CreateAsync(string scriptPath, string gitRef, IBuildLogger logger) =>
        CreateAsync(scriptPath, gitRef, logger, HostScratch.Root(), DirectoryName);

    /// <summary>Like the public overload, with the scratch folder and directory name given (tests).</summary>
    internal static async Task<RefWorktree> CreateAsync(
        string scriptPath, string gitRef, IBuildLogger logger, string scratchRoot, Func<string, string> directoryName)
    {
        var scriptDirectory = System.IO.Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;

        var repoRoot = await GitOutputAsync(logger, scriptDirectory, "rev-parse", "--show-toplevel")
            ?? throw new InvalidOperationException($"--ref needs a git repository, but {scriptDirectory} is not in one.");
        var commit = await GitOutputAsync(logger, repoRoot, "rev-parse", "--verify", "--quiet", $"{gitRef}^{{commit}}")
            ?? throw new InvalidOperationException($"--ref {gitRef}: no such commit, tag or branch.");

        var relativeScript = System.IO.Path.GetRelativePath(repoRoot, scriptPath).Replace('\\', '/');
        if (await GitOutputAsync(logger, repoRoot, "cat-file", "-e", $"{commit}:{relativeScript}") == null)
        {
            throw new InvalidOperationException(
                $"--ref {gitRef}: the build script {relativeScript} does not exist in that commit.");
        }

        Directory.CreateDirectory(scratchRoot);
        await SweepStaleAsync(scratchRoot, repoRoot, logger);

        // Take the worktree's lock before creating it. A concurrent sweep can grab a
        // just-created lock file for a moment; then try a new name instead of running unlocked.
        string path = "";
        FileStream? lockStream = null;
        for (var attempt = 0; attempt < 3 && lockStream == null; attempt++)
        {
            path = System.IO.Path.Combine(scratchRoot, directoryName(commit));
            lockStream = TryLock(path + ".lock");
        }
        if (lockStream == null)
        {
            throw new InvalidOperationException(
                $"--ref {gitRef}: could not take the lock file {path}.lock in {scratchRoot}. Set {HostScratch.EnvVar} to use another folder.");
        }
        var existedBefore = Directory.Exists(path);

        var add = await GitAsync(logger, repoRoot, "worktree", "add", "--detach", "--quiet", path, commit);
        if (!add.Success)
        {
            // Remove what this attempt left behind, never a directory that was already there.
            if (!existedBefore)
            {
                TryDeleteDirectory(path);
            }
            await GitAsync(logger, repoRoot, "worktree", "prune");
            lockStream.Dispose();
            TryDeleteFile(path + ".lock");
            throw new InvalidOperationException(AddFailedMessage(gitRef, path, scratchRoot, add));
        }

        var worktreeScript = System.IO.Path.Combine(path, relativeScript);
        return new RefWorktree(repoRoot, path, commit, worktreeScript, lockStream, logger);
    }

    /// <summary>A short, unique worktree directory name for a commit.</summary>
    internal static string DirectoryName(string commit) => $"{Prefix}{commit[..12]}-{Guid.NewGuid().ToString("N")[..8]}";

    /// <summary>
    /// Removes stale worktrees in <paramref name="scratchRoot"/>: every ando-ref-*
    /// entry whose lock file exists and is not held by a running ando. Then prunes
    /// the worktree records of <paramref name="repoRoot"/> when given. Never sweeps
    /// a system temp folder. Every run calls it (without a repository); git also
    /// prunes the records of removed worktrees by itself.
    /// </summary>
    internal static async Task SweepStaleAsync(string scratchRoot, string? repoRoot, IBuildLogger logger)
    {
        if (HostScratch.IsSystemTemp(scratchRoot) || !Directory.Exists(scratchRoot))
        {
            return;
        }

        var removed = false;
        foreach (var lockFile in Directory.EnumerateFiles(scratchRoot, Prefix + "*.lock"))
        {
            using var stream = TryLock(lockFile, quiet: true);
            if (stream == null)
            {
                continue; // Held by a running ando, or unusable.
            }

            var dir = lockFile[..^".lock".Length];
            if (Directory.Exists(dir))
            {
                if (!TryDeleteDirectory(dir))
                {
                    logger.Warning($"Could not remove the stale --ref worktree {dir}.");
                    continue;
                }
                logger.Debug($"Removed the stale --ref worktree {dir}.");
                removed = true;
            }
            stream.Dispose();
            TryDeleteFile(lockFile);
        }

        if (removed && repoRoot != null)
        {
            await GitAsync(logger, repoRoot, "worktree", "prune");
        }
    }

    /// <summary>Removes the worktree.</summary>
    public async ValueTask DisposeAsync()
    {
        var remove = await GitAsync(_logger, _repoRoot, "worktree", "remove", "--force", Path);
        _lock?.Dispose();
        if (!remove.Success)
        {
            // The lock file stays, so the next --ref run removes the worktree.
            _logger.Warning($"Could not remove the --ref worktree {Path}. Remove it with: git worktree remove --force {Path}");
            return;
        }
        TryDeleteFile(Path + ".lock");
    }

    private static string AddFailedMessage(string gitRef, string path, string scratchRoot, CommandResult result)
    {
        var detail = (result.Error ?? result.Output ?? "").Trim();
        var message = $"--ref {gitRef}: could not create a worktree at {path}.";
        if (detail.Length > 0)
        {
            message += $" git: {detail}";
        }
        var free = FreeSpace(scratchRoot);
        if (free != null)
        {
            message += $" Free space in {scratchRoot}: {free}.";
        }
        return message + $" Set {HostScratch.EnvVar} to use another folder.";
    }

    // Free space of the file system holding the folder, or null when unknown.
    internal static string? FreeSpace(string path)
    {
        try
        {
            return HostScratch.Gb(new DriveInfo(path).AvailableFreeSpace);
        }
        catch (Exception)
        {
            return null;
        }
    }

    // Opens and exclusively locks the file (created if missing), or returns null
    // when another process holds it or it cannot be used.
    private static FileStream? TryLock(string path, bool quiet = false)
    {
        try
        {
            return quiet && !File.Exists(path)
                ? null
                : new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Harmless: a leftover lock file is cleaned up by the next sweep.
        }
    }

    // Runs git and returns trimmed stdout, or null when git fails.
    private static async Task<string?> GitOutputAsync(IBuildLogger logger, string workingDirectory, params string[] args)
    {
        var result = await GitAsync(logger, workingDirectory, args);
        return result.Success ? (result.Output ?? "").Trim() : null;
    }

    private static Task<CommandResult> GitAsync(IBuildLogger logger, string workingDirectory, params string[] args) =>
        new ProcessRunner(logger).ExecuteAsync("git", args, new CommandOptions
        {
            WorkingDirectory = workingDirectory,
            SuppressOutput = true,
            CaptureStderr = true,
            TimeoutMs = 120_000
        });
}
