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
// At the start of a run ANDO warns (without failing) when the folder holds more
// than WarnSizeBytes or its disk has less than WarnFreeBytes left.
// =============================================================================

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
        if (!string.IsNullOrWhiteSpace(envValue))
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

    /// <summary>
    /// A warning when <paramref name="root"/> holds more than <paramref name="maxBytes"/>
    /// or its disk has less than <paramref name="minFreeBytes"/> free, otherwise null.
    /// Never throws; the run goes on either way.
    /// </summary>
    internal static string? CheckUsage(string root, long maxBytes = WarnSizeBytes, long minFreeBytes = WarnFreeBytes)
    {
        try
        {
            var size = DirectorySize(root);
            if (size > maxBytes)
            {
                return $"The ANDO scratch folder {root} holds {Gb(size)} (more than {Gb(maxBytes)}). " +
                    "Leftovers of killed runs (ando-ref-*, ando-project-*, ando-release-*) can be removed when no ando run is active. " +
                    $"Set {EnvVar} to use another folder.";
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

    private static long DirectorySize(string root) =>
        new DirectoryInfo(root)
            .EnumerateFiles("*", new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint,
            })
            .Sum(f => f.Length);

    /// <summary>True when <paramref name="path"/> is the system temp folder.</summary>
    internal static bool IsSystemTemp(string path) =>
        string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
