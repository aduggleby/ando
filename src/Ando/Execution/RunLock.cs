// =============================================================================
// RunLock.cs
//
// Summary: File locks that keep two ando runs from using one thing at once.
//
// Two kinds of lock, both exclusive and non-blocking, kept until the process exits:
// - The container lock: one run per build container (taken by the CLI).
// - Named locks: Ando.Lock("name") in a script, e.g. one release at a time.
//
// Design Decisions:
// - A FileStream with FileShare.None: on Linux and macOS .NET takes
//   flock(LOCK_EX | LOCK_NB) and opens the file with O_CLOEXEC, so docker, git and
//   child ando processes do not inherit the lock. The OS drops it when the
//   process dies, so a crash never leaves a stale lock.
// - Locks live in ~/.ando/locks (ANDO_LOCK_DIR or the constructor argument override it): container-<name>.lock
//   for a build container and name-<sanitized>.lock for Ando.Lock, so the two kinds
//   never share a file. Names that sanitize to the same file are the same lock
// - Only a real "would block" (flock EWOULDBLOCK, or a Windows sharing violation) is
//   reported as held (exit 9). Any other I/O error fails with its message, and a lock
//   folder or file we may not use fails a named lock but only warns for the
//   container lock, which did not exist before and must not break existing setups
// - Child builds get the names held by their parent in ANDO_HELD_LOCKS
//   (newline separated) and skip them, so a shared helper does not block itself
// =============================================================================

namespace Ando.Execution;

/// <summary>Thrown when a lock is held by another run. The CLI maps it to exit code 9.</summary>
public class RunLockHeldException(string message) : InvalidOperationException(message);

/// <summary>
/// The locks one ando run holds. Disposing releases them.
/// </summary>
public sealed class RunLockSet : IDisposable
{
    /// <summary>Env var carrying the named locks a parent run holds to its child builds.</summary>
    public const string HeldLocksEnvVar = "ANDO_HELD_LOCKS";

    /// <summary>Env var that overrides the lock folder (default ~/.ando/locks); the test suite sets it.</summary>
    public const string LockDirEnvVar = "ANDO_LOCK_DIR";

    private readonly string _directory;
    private readonly HashSet<string> _inherited;
    private readonly List<string> _namedLocks = [];
    private readonly List<FileStream> _streams = [];
    private readonly object _gate = new();

    /// <param name="directory">Lock directory; null means ANDO_LOCK_DIR, else ~/.ando/locks.</param>
    /// <param name="inheritedLocks">Parent's ANDO_HELD_LOCKS value; null reads the environment.</param>
    public RunLockSet(string? directory = null, string? inheritedLocks = null)
    {
        var fromEnv = Environment.GetEnvironmentVariable(LockDirEnvVar);
        _directory = directory
            ?? (!string.IsNullOrWhiteSpace(fromEnv) ? fromEnv : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".ando", "locks"));
        _inherited = (inheritedLocks ?? Environment.GetEnvironmentVariable(HeldLocksEnvVar) ?? "")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Named locks this run holds itself (not inherited ones), in acquisition order.</summary>
    public IReadOnlyList<string> NamedLocks
    {
        get { lock (_gate) { return [.. _namedLocks]; } }
    }

    /// <summary>
    /// Value for ANDO_HELD_LOCKS in child builds: the inherited names plus the ones
    /// held here. Empty when there are none.
    /// </summary>
    public string HeldForChildren
    {
        get { lock (_gate) { return string.Join('\n', _inherited.Concat(_namedLocks).Distinct()); } }
    }

    /// <summary>
    /// Takes the lock of a build container. Throws <see cref="RunLockHeldException"/> when
    /// another run holds it. A lock folder or file that cannot be used is not fatal: the
    /// run goes on unlocked, as before the lock existed (see <see cref="LastWarning"/>).
    /// </summary>
    public void AcquireContainer(string containerName)
    {
        var path = PathFor("container-", containerName);
        try
        {
            Directory.CreateDirectory(_directory);
            Take(path, $"Another ando run uses container {containerName} (lock file {path}). Wait for it to finish, or stop it.");
        }
        catch (UnauthorizedAccessException ex)
        {
            LastWarning = $"Could not use the lock folder {_directory} ({ex.Message}); running without a container lock.";
        }
        catch (IOException ex)
        {
            LastWarning = $"Could not use the lock folder {_directory} ({ex.Message}); running without a container lock.";
        }
    }

    /// <summary>Set when the container lock was skipped; the CLI logs it as a warning.</summary>
    public string? LastWarning { get; private set; }

    /// <summary>
    /// Takes a named lock from Ando.Lock. A name already held (by this run or a parent
    /// run) is a no-op; so is a name that sanitizes to the same lock file as a held one.
    /// Returns true when the lock was newly taken here.
    /// </summary>
    public bool AcquireNamed(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Contains('\n') || name.Contains('\r'))
        {
            throw new ArgumentException("A lock name must not be empty or contain line breaks.", nameof(name));
        }

        var key = Sanitize(name);
        lock (_gate)
        {
            if (_inherited.Any(n => Sanitize(n) == key) || _namedLocks.Any(n => Sanitize(n) == key))
            {
                return false;
            }
        }

        var path = PathFor("name-", name);
        try
        {
            Directory.CreateDirectory(_directory);
            Take(path, $"Lock \"{name}\" is held by another ando run (lock file {path}). Wait for it to finish, or stop it.");
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException($"Could not take lock \"{name}\": no permission for the lock folder or file {path} ({ex.Message}).");
        }
        catch (IOException ex)
        {
            throw new InvalidOperationException($"Could not take lock \"{name}\" (lock file {path}): {ex.Message}");
        }

        lock (_gate)
        {
            _namedLocks.Add(name);
        }
        return true;
    }

    private string PathFor(string prefix, string name) => Path.Combine(_directory, prefix + Sanitize(name) + ".lock");

    // Keeps the file name safe: letters, digits, '.', '-' and '_' stay, the rest becomes '_'.
    internal static string Sanitize(string name) =>
        new(name.Select(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_').ToArray());

    // True when an IOException from opening a lock file means "another process holds it":
    // EWOULDBLOCK/EAGAIN (the raw errno is the HResult on Unix: 11 on Linux, 35 on macOS) or a
    // Windows sharing (32) / lock (33) violation. Anything else is a real error.
    internal static bool IsWouldBlock(IOException ex)
    {
        if (OperatingSystem.IsWindows())
        {
            return (ex.HResult & 0xFFFF) is 32 or 33;
        }
        return ex.HResult is 11 or 35;
    }

    private void Take(string path, string heldMessage)
    {
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex) when (IsWouldBlock(ex))
        {
            throw new RunLockHeldException(heldMessage);
        }

        lock (_gate)
        {
            _streams.Add(stream);
        }
    }

    public void Dispose()
    {
        FileStream[] streams;
        lock (_gate)
        {
            streams = [.. _streams];
            _streams.Clear();
        }

        foreach (var stream in streams)
        {
            stream.Dispose();
        }
    }
}
