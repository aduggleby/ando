// =============================================================================
// HostScratch.cs
//
// Summary: The host folder for ANDO's large temporary files.
//
// --ref worktrees, the project tar copied into the build container and staged
// release assets go here. The default is $XDG_CACHE_HOME/ando/tmp (else
// ~/.cache/ando/tmp), which is disk-backed, because /tmp is often a small tmpfs
// shared with other tools and a full /tmp made --ref runs fail. ANDO_TMPDIR
// overrides it. TMPDIR is ignored on purpose: it often points back into /tmp.
// At the start of a run ANDO removes leftovers of killed runs and warns (without
// failing) when its own entries there hold more
// than WarnSizeBytes or the disk has less than WarnFreeBytes left.
// =============================================================================

using System.Text.RegularExpressions;

namespace Ando.Execution;

/// <summary>
/// Resolves the host folder for ANDO's large temporary files.
/// </summary>
public static class HostScratch
{
    /// <summary>Environment variable that overrides the folder.</summary>
    public const string EnvVar = "ANDO_TMPDIR";

    /// <summary>
    /// Returns the folder, created if needed: ANDO_TMPDIR, else
    /// $XDG_CACHE_HOME/ando/tmp, else ~/.cache/ando/tmp. Falls
    /// back to the system temp folder when there is no home folder or the folder
    /// cannot be created.
    /// </summary>
    public static string Root()
    {
        var path = Resolve(
            Environment.GetEnvironmentVariable(EnvVar),
            Environment.GetEnvironmentVariable("XDG_CACHE_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        if (path != null)
        {
            try
            {
                Directory.CreateDirectory(path);
                return path;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Fall through to the system temp folder.
            }
        }
        return Path.GetTempPath();
    }

    /// <summary>
    /// The folder for the given ANDO_TMPDIR, XDG_CACHE_HOME and home folder, as an
    /// absolute path, or null when none is usable. A relative XDG_CACHE_HOME is
    /// ignored, as the XDG spec requires.
    /// </summary>
    internal static string? Resolve(string? envValue, string? xdgCacheHome, string? home)
    {
        // A relative ANDO_TMPDIR is ignored: child builds run in other folders.
        if (!string.IsNullOrWhiteSpace(envValue) && Path.IsPathRooted(envValue))
        {
            return Path.GetFullPath(envValue);
        }
        if (!string.IsNullOrWhiteSpace(xdgCacheHome) && Path.IsPathRooted(xdgCacheHome))
        {
            return Path.Combine(xdgCacheHome, "ando", "tmp");
        }
        if (!string.IsNullOrWhiteSpace(home) && Path.IsPathRooted(home))
        {
            return Path.Combine(home, ".cache", "ando", "tmp");
        }
        return null;
    }

    /// <summary>Folder size above which a run warns (20 GB).</summary>
    public const long WarnSizeBytes = 20L * 1024 * 1024 * 1024;

    /// <summary>Free disk space below which a run warns (5 GB).</summary>
    public const long WarnFreeBytes = 5L * 1024 * 1024 * 1024;

    // Exact names of the entries ANDO creates, so a shared ANDO_TMPDIR (even $HOME)
    // never has someone else's "ando-release-tools" counted or deleted.
    private static readonly Regex WorktreeName = new(@"^ando-ref-[0-9a-f]{12}-[0-9a-f]{8}(\.lock)?$", RegexOptions.CultureInvariant);
    private static readonly Regex ProjectFileName = new(@"^ando-project-(files-)?[0-9a-f]{32}\.(tar|lst)$", RegexOptions.CultureInvariant);
    private static readonly Regex ReleaseDirName = new(@"^ando-release-[0-9a-f]{32}$", RegexOptions.CultureInvariant);

    /// <summary>True for a --ref worktree folder or its lock file name.</summary>
    internal static bool IsWorktreeName(string name) => WorktreeName.IsMatch(name);

    /// <summary>
    /// A warning when ANDO's entries in <paramref name="root"/> hold more than <paramref name="maxBytes"/>
    /// or its disk has less than <paramref name="minFreeBytes"/> free, otherwise null.
    /// Never throws; the run goes on either way.
    /// </summary>
    internal static string? CheckUsage(string root, long maxBytes = WarnSizeBytes, long minFreeBytes = WarnFreeBytes)
    {
        try
        {
            var size = AndoEntriesSize(root);
            if (size > maxBytes)
            {
                return $"ANDO's files in the scratch folder {root} (ando-ref-*, ando-project-*, ando-release-*) take {Gb(size)} (more than {Gb(maxBytes)}). " +
                    $"Entries no running ando uses can be removed. Set {EnvVar} to use another folder.";
            }
            var free = new DriveInfo(root).AvailableFreeSpace;
            if (free < minFreeBytes)
            {
                return $"Only {Gb(free)} free on the disk of the ANDO scratch folder {root}. --ref runs may fail. Set {EnvVar} to use another folder.";
            }
        }
        catch (Exception)
        {
            // A check that cannot run is not worth a warning.
        }
        return null;
    }

    internal static string Gb(long bytes) => $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";

    // Total size of ANDO's top-level entries only, so a shared folder never gets
    // walked or blamed for other tools' files.
    private static long AndoEntriesSize(string root)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
        };
        long total = 0;
        foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
        {
            if (!IsAndoEntry(entry.Name)) continue;
            total += entry is DirectoryInfo dir
                ? dir.EnumerateFiles("*", options).Sum(f => f.Length)
                : ((FileInfo)entry).Length;
        }
        return total;
    }

    internal static bool IsAndoEntry(string name) =>
        WorktreeName.IsMatch(name) || ProjectFileName.IsMatch(name) || ReleaseDirName.IsMatch(name);

    /// <summary>
    /// Removes ando-project-* and ando-release-* entries older than <paramref name="maxAge"/>
    /// (default one day): leftovers of killed runs, since a live run deletes its own within
    /// minutes. Never touches a system temp folder. Never throws.
    /// </summary>
    internal static void SweepOldFiles(string root, TimeSpan? maxAge = null)
    {
        if (IsSystemTemp(root)) return;
        var cutoff = DateTime.UtcNow - (maxAge ?? TimeSpan.FromDays(1));
        try
        {
            foreach (var entry in new DirectoryInfo(root).EnumerateFileSystemInfos())
            {
                var ours = entry is DirectoryInfo
                    ? ReleaseDirName.IsMatch(entry.Name)
                    : ProjectFileName.IsMatch(entry.Name);
                if (!ours || entry.LastWriteTimeUtc > cutoff)
                {
                    continue;
                }
                try
                {
                    if (entry is DirectoryInfo dir) dir.Delete(recursive: true);
                    else entry.Delete();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Try again next run.
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Folder missing or unreadable: nothing to sweep.
        }
    }

    /// <summary>
    /// True when <paramref name="path"/> is a system temp folder: the current
    /// temp folder (which follows TMPDIR), /tmp, /var/tmp or /private/tmp.
    /// </summary>
    internal static bool IsSystemTemp(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var normalized = Normalize(path);
        string[] temps = [Normalize(Path.GetTempPath()), "/tmp", "/var/tmp", "/private/tmp"];
        return temps.Any(t => string.Equals(normalized, t, comparison));
    }

    private static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
