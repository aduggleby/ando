// =============================================================================
// ContainerExecutor.cs
//
// Summary: Executes commands inside a Docker container via 'docker exec'.
//
// ContainerExecutor implements ICommandExecutor to run commands inside an
// existing Docker container. It translates host paths to container paths and
// provides the same real-time output streaming as ProcessRunner.
//
// Architecture:
// - Wraps commands in 'docker exec' calls to the target container
// - Handles path translation between host and container (/workspace mount)
// - Preserves environment variables and working directory settings
// - Output streaming works the same as local execution for consistency
//
// Design Decisions:
// - Implements ICommandExecutor so build logic is execution-agnostic
// - Container must be pre-created by DockerManager (separation of concerns)
// - Uses -w flag for working directory rather than 'cd' commands
// - Uses 'which' command to check tool availability in container
// =============================================================================

using System.Diagnostics;
using Ando.Logging;

namespace Ando.Execution;

/// <summary>
/// Executes commands inside a Docker container via 'docker exec'.
/// Provides real-time output streaming and working directory management.
/// Implements ICommandExecutor for seamless switching between local and container execution.
/// </summary>
public class ContainerExecutor : CommandExecutorBase
{
    private readonly string _containerId;
    private readonly string _containerWorkDir;
    private readonly string _containerRootPath;
    private string? _hostRootPath;

    /// <summary>
    /// Creates a new ContainerExecutor.
    /// </summary>
    /// <param name="containerId">Docker container ID or name (container must be running)</param>
    /// <param name="logger">Logger for output streaming</param>
    /// <param name="containerWorkDir">Working directory inside the container (default: /workspace)</param>
    public ContainerExecutor(string containerId, IBuildLogger logger, string containerWorkDir = "/workspace")
        : base(logger)
    {
        _containerId = containerId;
        _containerWorkDir = containerWorkDir;
        _containerRootPath = containerWorkDir; // Container root is the workspace mount point
    }

    /// <summary>
    /// Sets the host root path for path translation.
    /// Call this after construction when the host project path is known.
    /// </summary>
    /// <param name="hostRootPath">Absolute path to project root on the host.</param>
    public void SetHostRootPath(string hostRootPath)
    {
        _hostRootPath = Path.GetFullPath(hostRootPath);
    }

    protected override ProcessStartInfo PrepareProcessStartInfo(string command, string[] args, CommandOptions options)
    {
        // Build the 'docker exec' command with appropriate flags.
        var dockerArgs = new List<string> { "exec" };

        // Set working directory using -w flag.
        // Container paths use /workspace as the project root.
        var workDir = options.WorkingDirectory ?? _containerWorkDir;
        dockerArgs.AddRange(["-w", ConvertToContainerPath(workDir)]);

        // Pass environment variables using -e flags.
        foreach (var (key, value) in options.Environment)
        {
            dockerArgs.AddRange(["-e", $"{key}={value}"]);
        }

        // Append container ID and the actual command to execute.
        dockerArgs.Add(_containerId);
        dockerArgs.Add(command);
        dockerArgs.AddRange(args);

        var startInfo = new ProcessStartInfo { FileName = "docker" };

        foreach (var arg in dockerArgs)
        {
            startInfo.ArgumentList.Add(arg);
        }

        return startInfo;
    }

    /// <summary>
    /// Checks if a command is available inside the container.
    /// Uses 'which' command to locate the binary.
    /// </summary>
    public override bool IsAvailable(string command)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "exec", _containerId, "which", command },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    // Converts a host path to a container path.
    // The project is mounted at /workspace in the container, so paths
    // need to be translated accordingly.
    private string ConvertToContainerPath(string path)
    {
        // Already a container path - no conversion needed.
        if (path.StartsWith(_containerRootPath))
        {
            return path;
        }

        // Relative paths are relative to the container working directory.
        if (!Path.IsPathRooted(path))
        {
            return $"{_containerWorkDir}/{path}";
        }

        // For host absolute paths, translate to container path if possible.
        if (_hostRootPath != null)
        {
            var fullPath = Path.GetFullPath(path);

            // Check if the path is within the host project root.
            // Use Ordinal comparison because Linux paths are case-sensitive.
            if (fullPath.StartsWith(_hostRootPath, StringComparison.Ordinal))
            {
                var relativePath = Path.GetRelativePath(_hostRootPath, fullPath);
                // Convert Windows separators to Unix if needed
                relativePath = relativePath.Replace('\\', '/');
                return $"{_containerRootPath}/{relativePath}";
            }

            // Path is outside the project root - this will fail in the container
            Logger.Warning($"Path '{path}' is outside the project root and may not be accessible in the container.");
        }

        // Pass through unchanged as fallback (may fail if path doesn't exist in container)
        return path;
    }
}
