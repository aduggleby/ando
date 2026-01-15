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
// Usage in build.ando:
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
/// Manages build artifact registration and copying from container to host.
/// </summary>
public class ArtifactOperations
{
    private readonly IBuildLogger _logger;
    private readonly List<ArtifactEntry> _artifacts = new();

    public ArtifactOperations(IBuildLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Read-only list of registered artifacts.
    /// </summary>
    public IReadOnlyList<ArtifactEntry> Artifacts => _artifacts;

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

            // Ensure host directory exists.
            var hostDir = Path.GetDirectoryName(hostPath);
            if (!string.IsNullOrEmpty(hostDir))
            {
                Directory.CreateDirectory(hostDir);
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

    /// <summary>
    /// Clears all registered artifacts.
    /// </summary>
    public void Clear()
    {
        _artifacts.Clear();
    }
}
