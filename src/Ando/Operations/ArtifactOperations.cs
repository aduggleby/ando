// =============================================================================
// ArtifactOperations.cs
//
// Summary: Manages build artifact registration and copying from container to host.
//
// ArtifactOperations allows build scripts to specify which files or directories
// from the container's /workspace should be copied back to the host machine
// after a successful build. This is essential since all builds run in Docker
// with isolated workspaces (project files are copied in, not mounted).
//
// Architecture:
// - Scripts register artifacts using CopyToHost(containerPath, hostPath)
// - After build completes, CLI calls CopyToHostAsync to copy all registered artifacts
// - Uses 'docker cp' command to copy files from container to host
//
// Usage in build.csando:
//   Artifacts.CopyToHost("/workspace/artifacts/dist", "./dist");
//   Artifacts.CopyToHost("/workspace/bin/Release", "./output");
//
// Design Decisions:
// - Artifacts are registered during script execution, not copied immediately
// - This allows validation and batch copying after build success
// - All artifacts are copied via 'docker cp' since workspace is not mounted
// - Logging provides visibility into what files are being copied
// =============================================================================

using System.Diagnostics;
using Ando.Logging;

namespace Ando.Operations;

/// <summary>
/// Represents an artifact to copy from container to host.
/// </summary>
public record ArtifactEntry(
    string ContainerPath,
    string HostPath
);

/// <summary>
/// Represents a zipped artifact to copy from container to host.
/// The artifact will be zipped in the container, copied as a single file,
/// then extracted on the host.
/// </summary>
public record ZippedArtifactEntry(
    string ContainerPath,
    string HostPath
);

/// <summary>
/// Manages build artifact registration and copying from container to host.
/// </summary>
public class ArtifactOperations
{
    private readonly IBuildLogger _logger;
    private readonly List<ArtifactEntry> _artifacts = new();
    private readonly List<ZippedArtifactEntry> _zippedArtifacts = new();

    public ArtifactOperations(IBuildLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Read-only list of registered artifacts.
    /// </summary>
    public IReadOnlyList<ArtifactEntry> Artifacts => _artifacts;

    /// <summary>
    /// Read-only list of registered zipped artifacts.
    /// </summary>
    public IReadOnlyList<ZippedArtifactEntry> ZippedArtifacts => _zippedArtifacts;

    /// <summary>
    /// Registers an artifact to be copied from the container to the host.
    /// The actual copy happens after build completion via CopyToHostAsync().
    /// </summary>
    /// <param name="containerPath">Path inside the container (relative to /workspace or absolute).</param>
    /// <param name="hostPath">Path on the host machine (relative to project root or absolute).</param>
    public void CopyToHost(string containerPath, string hostPath)
    {
        // Normalize container paths to be absolute.
        // Paths starting with / are kept as-is, others are relative to /workspace.
        var normalizedContainerPath = containerPath.StartsWith("/")
            ? containerPath
            : $"/workspace/{containerPath.TrimStart('.', '/')}";

        _artifacts.Add(new ArtifactEntry(normalizedContainerPath, hostPath));
        _logger.Debug($"Registered artifact: {normalizedContainerPath} -> {hostPath}");
    }

    /// <summary>
    /// Registers an artifact to be zipped in the container and copied to the host.
    /// The artifact is zipped before transfer for faster copying of many small files.
    /// The actual zip/copy/extract happens after build completion via CopyZippedToHostAsync().
    /// </summary>
    /// <param name="containerPath">Path inside the container (relative to /workspace or absolute).</param>
    /// <param name="hostPath">Path on the host machine (relative to project root or absolute).</param>
    public void CopyZippedToHost(string containerPath, string hostPath)
    {
        // Normalize container paths to be absolute.
        var normalizedContainerPath = containerPath.StartsWith("/")
            ? containerPath
            : $"/workspace/{containerPath.TrimStart('.', '/')}";

        _zippedArtifacts.Add(new ZippedArtifactEntry(normalizedContainerPath, hostPath));
        _logger.Debug($"Registered zipped artifact: {normalizedContainerPath} -> {hostPath}");
    }

    /// <summary>
    /// Copies all registered artifacts from the container to the host.
    /// Called by BuildContext after successful build completion.
    /// </summary>
    /// <remarks>
    /// Since the workspace is not mounted (files are copied in for isolation),
    /// all artifacts must be copied back to the host via 'docker cp'.
    /// </remarks>
    internal async Task CopyToHostAsync(string containerId, string projectRoot, IBuildLogger logger)
    {
        if (_artifacts.Count == 0)
        {
            logger.Debug("No artifacts registered for copying");
            return;
        }

        logger.Info($"Copying {_artifacts.Count} artifact(s) from container:");

        foreach (var artifact in _artifacts)
        {
            // Resolve host path relative to project root if not absolute.
            var hostPath = Path.IsPathRooted(artifact.HostPath)
                ? artifact.HostPath
                : Path.Combine(projectRoot, artifact.HostPath.TrimStart('.', '/'));

            // Ensure parent directory exists.
            var hostDir = Path.GetDirectoryName(hostPath);
            if (!string.IsNullOrEmpty(hostDir))
            {
                Directory.CreateDirectory(hostDir);
            }

            // Remove existing destination to ensure clean copy.
            // docker cp behavior: if dest exists as directory, it copies INTO it (creating dest/source-name)
            // We want to REPLACE the destination, not copy into it.
            try
            {
                if (Directory.Exists(hostPath))
                {
                    DeleteDirectoryRecursive(hostPath);
                }
                else if (File.Exists(hostPath))
                {
                    ClearReadOnlyAndDelete(hostPath);
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"  Could not remove existing destination: {ex.Message}");
                // Continue anyway - docker cp might still work or give a better error
            }

            logger.Info($"  {artifact.ContainerPath} -> {hostPath}");

            var success = await CopyFromContainerAsync(containerId, artifact.ContainerPath, hostPath, logger);
            if (success)
            {
                logger.Debug($"  Successfully copied: {artifact.ContainerPath}");
            }
            else
            {
                logger.Warning($"  Failed to copy: {artifact.ContainerPath}");
            }
        }
    }

    /// <summary>
    /// Copies all registered zipped artifacts from the container to the host.
    /// For each artifact: zips in container, copies zip to host, extracts, removes zip.
    /// </summary>
    internal async Task CopyZippedToHostAsync(string containerId, string projectRoot, IBuildLogger logger)
    {
        if (_zippedArtifacts.Count == 0)
        {
            logger.Debug("No zipped artifacts registered for copying");
            return;
        }

        logger.Info($"Copying {_zippedArtifacts.Count} zipped artifact(s) from container:");

        foreach (var artifact in _zippedArtifacts)
        {
            // Resolve host path relative to project root if not absolute.
            var hostPath = Path.IsPathRooted(artifact.HostPath)
                ? artifact.HostPath
                : Path.Combine(projectRoot, artifact.HostPath.TrimStart('.', '/'));

            // Ensure parent directory exists.
            var hostDir = Path.GetDirectoryName(hostPath);
            if (!string.IsNullOrEmpty(hostDir))
            {
                Directory.CreateDirectory(hostDir);
            }

            // Remove existing destination to ensure clean copy.
            try
            {
                if (Directory.Exists(hostPath))
                {
                    DeleteDirectoryRecursive(hostPath);
                }
                else if (File.Exists(hostPath))
                {
                    ClearReadOnlyAndDelete(hostPath);
                }
            }
            catch (Exception ex)
            {
                logger.Warning($"  Could not remove existing destination: {ex.Message}");
            }

            // Determine actual output path for logging.
            var isArchive = hostPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                           hostPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);
            var displayPath = isArchive ? hostPath : Path.Combine(hostPath, "artifacts.tar.gz");
            logger.Info($"  {artifact.ContainerPath} -> {displayPath}");

            var success = await CopyZippedFromContainerAsync(containerId, artifact.ContainerPath, hostPath, logger);
            if (success)
            {
                logger.Debug($"  Successfully copied: {artifact.ContainerPath}");
            }
            else
            {
                logger.Warning($"  Failed to copy: {artifact.ContainerPath}");
            }
        }
    }

    // Creates archive in container and copies to host as a single file.
    // Supports both .tar.gz and .zip formats based on hostPath extension.
    // If hostPath is a directory (no archive extension), uses "artifacts.tar.gz" as default.
    private static async Task<bool> CopyZippedFromContainerAsync(
        string containerId,
        string containerPath,
        string hostPath,
        IBuildLogger logger)
    {
        // Determine archive format from hostPath extension.
        var isZip = hostPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        var isTarGz = hostPath.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase);

        // If hostPath doesn't have an archive extension, treat it as a directory
        // and use default filename "artifacts.tar.gz".
        string finalHostPath;
        if (!isZip && !isTarGz)
        {
            finalHostPath = Path.Combine(hostPath, "artifacts.tar.gz");
            isTarGz = true;
        }
        else
        {
            finalHostPath = hostPath;
        }

        // Generate a unique filename in the container's /tmp directory.
        var extension = isZip ? ".zip" : ".tar.gz";
        var archiveName = $"ando-artifact-{Guid.NewGuid():N}{extension}";
        var containerArchivePath = $"/tmp/{archiveName}";

        try
        {
            // Step 1: Create archive in container.
            bool archiveSuccess;
            if (isZip)
            {
                logger.Debug($"Creating zip in container: {containerPath} -> {containerArchivePath}");
                archiveSuccess = await CreateZipInContainerAsync(containerId, containerPath, containerArchivePath, logger);
            }
            else
            {
                logger.Debug($"Creating tar.gz in container: {containerPath} -> {containerArchivePath}");
                archiveSuccess = await CreateTarInContainerAsync(containerId, containerPath, containerArchivePath, logger);
            }

            if (!archiveSuccess)
            {
                // Error already logged in CreateTarInContainerAsync/CreateZipInContainerAsync
                return false;
            }

            // Step 2: Ensure host directory exists.
            var hostDir = Path.GetDirectoryName(finalHostPath);
            if (!string.IsNullOrEmpty(hostDir))
            {
                Directory.CreateDirectory(hostDir);
            }

            // Step 2.5: Remove existing file if it exists.
            // Docker cp may fail with permission denied if the file is owned by root.
            if (File.Exists(finalHostPath))
            {
                try
                {
                    ClearReadOnlyAndDelete(finalHostPath);
                    logger.Debug($"Removed existing file: {finalHostPath}");
                }
                catch (UnauthorizedAccessException)
                {
                    // File might be owned by root from previous docker cp.
                    logger.Warning($"    Cannot remove existing file (permission denied): {finalHostPath}");
                    logger.Warning($"    File may be owned by root. Try: sudo rm -f \"{finalHostPath}\"");
                    return false;
                }
                catch (Exception ex)
                {
                    logger.Warning($"    Could not remove existing file: {ex.Message}");
                    // Continue anyway - docker cp might give a more useful error
                }
            }

            // Step 3: Copy archive to host.
            logger.Debug($"Copying archive to: {finalHostPath}");
            var (copySuccess, copyError) = await CopyFromContainerWithErrorAsync(containerId, containerArchivePath, finalHostPath, logger);
            if (!copySuccess)
            {
                logger.Warning($"    docker cp failed: {copyError}");
                return false;
            }

            // Step 3.5: Fix ownership of copied file.
            // docker cp creates files as root:root, which causes permission issues on subsequent runs.
            await FixFileOwnershipAsync(finalHostPath, logger);

            // Step 4: Clean up container temp file.
            logger.Debug("Cleaning up container temp files");
            await RemoveFileInContainerAsync(containerId, containerArchivePath, logger);

            return true;
        }
        catch (Exception ex)
        {
            logger.Debug($"Zipped copy failed: {ex.Message}");

            // Clean up on failure.
            try { await RemoveFileInContainerAsync(containerId, containerArchivePath, logger); } catch { }

            return false;
        }
    }

    // Creates a tar.gz file in the container using docker exec.
    // Uses tar which is universally available on Linux systems.
    private static async Task<bool> CreateTarInContainerAsync(
        string containerId,
        string sourcePath,
        string tarPath,
        IBuildLogger logger)
    {
        // Get the parent directory and the name of the source to tar.
        var parentDir = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "/workspace";
        var sourceName = Path.GetFileName(sourcePath);

        // Use tar command to create gzipped archive.
        // -c create, -z gzip, -f file, -C change to directory first.
        var command = $"tar -czf {tarPath} -C {parentDir} {sourceName}";
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "exec", containerId, "sh", "-c", command },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        logger.Debug($"Executing: docker exec {containerId} sh -c \"{command}\"");

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            logger.Error("Failed to start docker exec process");
            return false;
        }

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            logger.Warning($"    tar command failed: {error.Trim()}");
            return false;
        }

        return true;
    }

    // Creates a zip file in the container using docker exec.
    // Installs zip if not available, then creates the archive.
    private static async Task<bool> CreateZipInContainerAsync(
        string containerId,
        string sourcePath,
        string zipPath,
        IBuildLogger logger)
    {
        // Get the parent directory and the name of the source to zip.
        var parentDir = Path.GetDirectoryName(sourcePath)?.Replace('\\', '/') ?? "/workspace";
        var sourceName = Path.GetFileName(sourcePath);

        // Install zip if not available, then create archive.
        // -r recursive, -q quiet.
        var command = $"(command -v zip >/dev/null || apt-get update -qq && apt-get install -y -qq zip) && cd {parentDir} && zip -rq {zipPath} {sourceName}";
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "exec", containerId, "sh", "-c", command },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        logger.Debug($"Executing: docker exec {containerId} sh -c \"cd {parentDir} && zip -rq {zipPath} {sourceName}\"");

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            logger.Error("Failed to start docker exec process");
            return false;
        }

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            logger.Warning($"    zip command failed: {error.Trim()}");
            return false;
        }

        return true;
    }

    // Removes a file in the container using docker exec.
    private static async Task RemoveFileInContainerAsync(
        string containerId,
        string filePath,
        IBuildLogger logger)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "exec", containerId, "rm", "-f", filePath },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }
    }

    // Executes 'docker cp' to copy files from container to host.
    private static async Task<bool> CopyFromContainerAsync(
        string containerId,
        string containerPath,
        string hostPath,
        IBuildLogger logger)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "cp", $"{containerId}:{containerPath}", hostPath },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        logger.Debug($"Executing: docker cp {containerId}:{containerPath} {hostPath}");

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            logger.Error("Failed to start docker cp process");
            return false;
        }

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            logger.Debug($"docker cp failed: {error}");
            return false;
        }

        return true;
    }

    // Executes 'docker cp' and returns both success status and error message.
    private static async Task<(bool Success, string Error)> CopyFromContainerWithErrorAsync(
        string containerId,
        string containerPath,
        string hostPath,
        IBuildLogger logger)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "cp", $"{containerId}:{containerPath}", hostPath },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        logger.Debug($"Executing: docker cp {containerId}:{containerPath} {hostPath}");

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return (false, "Failed to start docker cp process");
        }

        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0)
        {
            return (false, error.Trim());
        }

        return (true, string.Empty);
    }

    /// <summary>
    /// Clears all registered artifacts.
    /// </summary>
    public void Clear()
    {
        _artifacts.Clear();
        _zippedArtifacts.Clear();
    }

    // Recursively deletes a directory, handling read-only files.
    private static void DeleteDirectoryRecursive(string path)
    {
        // First, recursively handle all subdirectories.
        foreach (var dir in Directory.GetDirectories(path))
        {
            DeleteDirectoryRecursive(dir);
        }

        // Then delete all files, clearing read-only attributes first.
        foreach (var file in Directory.GetFiles(path))
        {
            ClearReadOnlyAndDelete(file);
        }

        // Finally, delete the directory itself.
        Directory.Delete(path, false);
    }

    // Clears read-only attribute and deletes a file.
    private static void ClearReadOnlyAndDelete(string filePath)
    {
        var attributes = File.GetAttributes(filePath);
        if ((attributes & FileAttributes.ReadOnly) == FileAttributes.ReadOnly)
        {
            File.SetAttributes(filePath, attributes & ~FileAttributes.ReadOnly);
        }
        File.Delete(filePath);
    }

    // Fixes file ownership after docker cp (which creates files as root:root).
    // Uses chown to change ownership to the current user.
    private static async Task FixFileOwnershipAsync(string filePath, IBuildLogger logger)
    {
        // Only attempt on Unix systems.
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            return;
        }

        try
        {
            var uid = Environment.GetEnvironmentVariable("UID") ?? GetCurrentUid();
            var gid = Environment.GetEnvironmentVariable("GID") ?? GetCurrentGid();

            if (string.IsNullOrEmpty(uid))
            {
                return; // Can't determine current user
            }

            var startInfo = new ProcessStartInfo
            {
                FileName = "chown",
                ArgumentList = { $"{uid}:{gid}", filePath },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                if (process.ExitCode == 0)
                {
                    logger.Debug($"Fixed ownership of: {filePath}");
                }
            }
        }
        catch
        {
            // Ignore errors - chown might not be available or might fail
        }
    }

    // Gets the current user's UID using the 'id' command.
    private static string? GetCurrentUid()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "id",
                ArgumentList = { "-u" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 ? output : null;
            }
        }
        catch { }
        return null;
    }

    // Gets the current user's GID using the 'id' command.
    private static string? GetCurrentGid()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "id",
                ArgumentList = { "-g" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                return process.ExitCode == 0 ? output : null;
            }
        }
        catch { }
        return null;
    }
}
