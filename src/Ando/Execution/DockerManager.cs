// =============================================================================
// DockerManager.cs
//
// Summary: Manages Docker container lifecycle for isolated build execution.
//
// DockerManager handles the creation, reuse, and cleanup of Docker containers
// for running builds in isolated, reproducible environments. It implements
// "warm containers" for performance - keeping containers running between builds.
//
// Architecture:
// - Creates containers with project files COPIED into /workspace (not mounted)
// - Package caches (NuGet/npm) persist inside the container via warm container reuse
// - Uses 'tail -f /dev/null' to keep containers running indefinitely
// - ContainerExecutor handles command execution within containers
//
// Design Decisions:
// - Project files are copied in (not mounted) for true isolation - Docker
//   operations cannot modify host files during build execution
// - Warm containers avoid the overhead of container creation on each build
// - Package caches persist in warm containers (no host directories needed)
// - Artifacts must be explicitly copied back to host after build completion
// =============================================================================

using System.Diagnostics;
using Ando.Logging;

namespace Ando.Execution;

/// <summary>
/// Container information and state.
/// Immutable record for safe passing between methods.
/// </summary>
public record ContainerInfo(
    string Id,
    string Name,
    bool IsRunning
);

/// <summary>
/// Configuration for creating a container.
/// Mutable to allow fluent configuration pattern.
/// </summary>
public class ContainerConfig
{
    /// <summary>
    /// Docker image to use. Must be set by the caller.
    /// The CLI defaults to ubuntu:22.04 if not specified via --image or Options.UseImage().
    /// </summary>
    public required string Image { get; set; }

    /// <summary>
    /// Container name. Used for warm container lookup and cleanup.
    /// </summary>
    public string Name { get; set; } = "ando-build";

    /// <summary>
    /// Project root directory for Docker volume mounts. In DinD mode, this is the
    /// HOST filesystem path that the Docker daemon uses for volume mounts.
    /// </summary>
    public required string ProjectRoot { get; set; }

    /// <summary>
    /// Local project root for file operations (tar, directory checks).
    /// In normal mode, this equals ProjectRoot. In DinD mode, this is the actual
    /// path inside the current container (e.g., /workspace) while ProjectRoot
    /// is the host path for Docker mounts.
    /// </summary>
    public string? LocalProjectRoot { get; set; }

    /// <summary>
    /// Gets the path to use for local file operations.
    /// </summary>
    public string GetLocalProjectRoot() => LocalProjectRoot ?? ProjectRoot;

    /// <summary>
    /// Whether to mount Docker socket for Docker-in-Docker support.
    /// Enable this for builds that create Docker images.
    /// </summary>
    public bool MountDockerSocket { get; set; } = false;

    /// <summary>
    /// Environment variables to set in the container.
    /// These are passed to the docker run command via -e flags.
    /// </summary>
    public Dictionary<string, string> Environment { get; set; } = new();
}

/// <summary>
/// Result of checking Docker availability on the system.
/// Used to provide specific, actionable error messages.
/// </summary>
public enum DockerAvailability
{
    /// <summary>Docker CLI is installed and daemon is running.</summary>
    Available,

    /// <summary>Docker CLI is not installed or not on PATH.</summary>
    CliNotInstalled,

    /// <summary>Docker CLI is installed but the daemon is not running.</summary>
    DaemonNotRunning
}

/// <summary>
/// Manages Docker container lifecycle for isolated build execution.
/// Implements warm container pattern for fast subsequent builds.
/// </summary>
public class DockerManager
{
    private readonly IBuildLogger _logger;
    private readonly ProcessRunner _processRunner;

    // Directories to exclude when copying project to container.
    // These are typically large, generated, or version-controlled directories.
    private static readonly string[] ExcludedDirectories =
    [
        ".git",
        "node_modules",
        "bin",
        "obj",
        ".vs",
        ".idea",
        "packages",
        "TestResults",
        "test-results",
        "coverage",
        ".pytest_cache",
        "__pycache__",
        "dist",
        "build",
        "target",
        // Keep .ando for cache directories
    ];

    public DockerManager(IBuildLogger logger)
    {
        _logger = logger;
        // Uses ProcessRunner for Docker CLI commands since Docker runs on host.
        _processRunner = new ProcessRunner(logger);
    }

    /// <summary>
    /// Checks Docker availability with a detailed failure reason.
    /// Distinguishes between CLI not installed and daemon not running
    /// to provide actionable, platform-specific error messages.
    /// </summary>
    public DockerAvailability CheckDockerAvailability()
    {
        if (!_processRunner.IsAvailable("docker"))
            return DockerAvailability.CliNotInstalled;

        // CLI exists - verify the daemon is reachable.
        // Use SuppressOutput to avoid dumping `docker info` output on failure.
        try
        {
            var result = _processRunner.ExecuteAsync("docker", ["info"],
                new CommandOptions { TimeoutMs = 5000, SuppressOutput = true }).GetAwaiter().GetResult();
            return result.ExitCode == 0
                ? DockerAvailability.Available
                : DockerAvailability.DaemonNotRunning;
        }
        catch
        {
            return DockerAvailability.DaemonNotRunning;
        }
    }

    /// <summary>
    /// Checks if Docker is available on the system.
    /// Convenience wrapper around <see cref="CheckDockerAvailability"/>.
    /// </summary>
    public bool IsDockerAvailable()
    {
        return CheckDockerAvailability() == DockerAvailability.Available;
    }

    /// <summary>
    /// Gets installation instructions for the current OS.
    /// </summary>
    public string GetDockerInstallInstructions()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "  macOS:   brew install --cask docker";
        }
        if (OperatingSystem.IsLinux())
        {
            return "  Linux:   curl -fsSL https://get.docker.com | sh";
        }
        if (OperatingSystem.IsWindows())
        {
            return "  Windows: winget install Docker.DockerDesktop";
        }
        return "  Visit: https://docs.docker.com/get-docker/";
    }

    /// <summary>
    /// Checks if the Docker socket is mounted in a container.
    /// Used to determine if an existing warm container supports --dind mode.
    /// </summary>
    public async Task<bool> HasDockerSocketMountedAsync(string containerId)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "inspect", containerId, "--format", "{{range .Mounts}}{{.Source}}{{\"\\n\"}}{{end}}" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process == null) return false;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0) return false;

        // Check if Docker socket is in the list of mounted sources.
        return output.Contains("/var/run/docker.sock");
    }

    /// <summary>
    /// Finds an existing warm container for the project.
    /// </summary>
    public async Task<ContainerInfo?> FindWarmContainerAsync(string containerName)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "ps", "-a", "--filter", $"name=^{containerName}$", "--format", "{{.ID}},{{.Names}},{{.State}}" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process == null) return null;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        if (process.ExitCode != 0 || string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        var parts = output.Trim().Split(',');
        if (parts.Length >= 3)
        {
            return new ContainerInfo(parts[0], parts[1], parts[2] == "running");
        }

        return null;
    }

    /// <summary>
    /// Creates a new container or starts an existing one.
    /// For warm containers, re-copies project files to ensure fresh state.
    /// If --dind is requested but existing container doesn't have Docker socket, recreates it.
    /// </summary>
    public async Task<ContainerInfo> EnsureContainerAsync(ContainerConfig config)
    {
        var existing = await FindWarmContainerAsync(config.Name);

        if (existing != null)
        {
            // Check if --dind is requested but the existing container doesn't have Docker socket.
            // This happens when a container was created without --dind and later --dind is needed.
            if (config.MountDockerSocket)
            {
                var hasSocket = await HasDockerSocketMountedAsync(existing.Id);
                if (!hasSocket)
                {
                    _logger.Info($"Recreating container '{config.Name}' to enable Docker-in-Docker mode...");
                    await RemoveContainerAsync(config.Name);
                    return await CreateContainerAsync(config);
                }
            }

            if (!existing.IsRunning)
            {
                _logger.Debug($"Starting existing container: {config.Name}");
                await StartContainerAsync(existing.Id);
                existing = existing with { IsRunning = true };
            }
            else
            {
                _logger.Debug($"Reusing running container: {config.Name}");
            }

            // For warm containers, re-copy project files to ensure fresh build state.
            // This maintains isolation while preserving cached dependencies.
            _logger.Info($"Syncing project files to container...");
            await CopyProjectToContainerAsync(existing.Id, config.GetLocalProjectRoot());

            return existing;
        }

        _logger.Debug($"Creating new container: {config.Name}");
        return await CreateContainerAsync(config);
    }

    // Creates a new container (project files copied in, caches persist in container).
    // Project files are copied in separately for isolation.
    // Container runs 'tail -f /dev/null' to stay alive indefinitely.
    private async Task<ContainerInfo> CreateContainerAsync(ContainerConfig config)
    {
        _logger.Info($"Creating Docker container '{config.Name}'");
        _logger.Info($"  Image: {config.Image}");
        _logger.Info($"  Project files will be copied (isolated mode)");

        // Build the docker run command arguments.
        // Note: Project root is NOT mounted - files are copied in for isolation.
        // Cache directories live INSIDE the container and persist via warm container reuse.
        var args = new List<string>
        {
            "run",
            "-d",                              // Detached mode
            "--name", config.Name,             // Named for easy lookup
            "-w", "/workspace",                // Set working directory
            "--entrypoint", "tail",            // Override entrypoint to keep alive
            // Configure environment variables for package manager caches inside container.
            // These caches persist because warm containers are reused across builds.
            "-e", "NUGET_PACKAGES=/workspace/.ando/cache/nuget",
            "-e", "npm_config_cache=/workspace/.ando/cache/npm",
        };

        _logger.Info($"  Container directories:");
        _logger.Info($"    /workspace                    - Project root (copied, isolated)");
        _logger.Info($"    /workspace/artifacts          - Build outputs");
        _logger.Info($"    /workspace/.ando/cache        - Package caches (persists in warm container)");

        // Optional: Mount Docker socket for Docker-in-Docker scenarios.
        // Required when builds need to create Docker images.
        // Note: /var/run/docker.sock works cross-platform. On Windows and macOS,
        // Docker Desktop translates this path internally so containers can always
        // use the Unix socket path regardless of the host OS.
        if (config.MountDockerSocket)
        {
            args.AddRange(["-v", "/var/run/docker.sock:/var/run/docker.sock"]);
            // Enable host.docker.internal on Linux (works by default on Docker Desktop)
            // This allows containers to reach services on the host (e.g., E2E test servers)
            args.AddRange(["--add-host", "host.docker.internal:host-gateway"]);
            _logger.Info($"  Docker socket mounted for Docker-in-Docker");
        }

        // Add custom environment variables.
        foreach (var (key, value) in config.Environment)
        {
            args.AddRange(["-e", $"{key}={value}"]);
        }

        // Add image and arguments to 'tail' command.
        args.Add(config.Image);
        args.AddRange(["-f", "/dev/null"]); // Keep container running forever

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start docker process");
        }

        // Read stdout and stderr in parallel to avoid deadlocks.
        // If we read sequentially (stdout then stderr), and the process writes
        // enough to stderr to fill the buffer, the process will block waiting
        // for stderr to be read while we're still waiting for stdout.
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var containerId = (await stdoutTask).Trim();
        var error = await stderrTask;

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Failed to create container: {error}");
        }

        _logger.Info($"  Container ID: {containerId[..12]}");

        // Create the /workspace directory in container.
        await ExecuteInContainerAsync(containerId, "mkdir", ["-p", "/workspace"]);

        // Copy project files into the container.
        // Use local path since tar operates on local files.
        _logger.Info($"Copying project files to container...");
        await CopyProjectToContainerAsync(containerId, config.GetLocalProjectRoot());

        return new ContainerInfo(containerId, config.Name, true);
    }

    /// <summary>
    /// Copies project files from host to container's /workspace directory.
    /// Excludes large/generated directories for efficiency.
    /// This is the key to isolation - the container works on a copy, not the original.
    /// </summary>
    public async Task CopyProjectToContainerAsync(string containerId, string projectRoot)
    {
        // Verify tar is available on the host before attempting to create the archive.
        if (!_processRunner.IsAvailable("tar"))
        {
            var message = "The 'tar' command is required but was not found.";
            if (OperatingSystem.IsWindows())
            {
                message += " Please ensure Windows 10 1803+ (which includes tar) or install tar via Git for Windows.";
            }
            throw new InvalidOperationException(message);
        }

        // First, clean the workspace (except cache directories).
        // This ensures a fresh state for each build.
        await ExecuteInContainerAsync(containerId, "sh", ["-c",
            "find /workspace -mindepth 1 -maxdepth 1 ! -name '.ando' -exec rm -rf {} +"]);

        // Create a temporary tar archive excluding unwanted directories.
        // Using tar is much faster than individual docker cp calls.
        var tarPath = Path.Combine(Path.GetTempPath(), $"ando-project-{Guid.NewGuid():N}.tar");
        var gitFileListPath = Path.Combine(Path.GetTempPath(), $"ando-project-files-{Guid.NewGuid():N}.lst");

        try
        {
            var tarArgs = await BuildTarArgumentsAsync(projectRoot, tarPath, gitFileListPath);

            // Create the tar archive on the host.
            var tarStartInfo = new ProcessStartInfo
            {
                FileName = "tar",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var arg in tarArgs)
            {
                tarStartInfo.ArgumentList.Add(arg);
            }

            using (var tarProcess = Process.Start(tarStartInfo))
            {
                if (tarProcess == null)
                {
                    throw new InvalidOperationException("Failed to start tar process");
                }

                await tarProcess.WaitForExitAsync();
                if (tarProcess.ExitCode != 0)
                {
                    var tarError = await tarProcess.StandardError.ReadToEndAsync();
                    _logger.Warning($"Tar had warnings (may be normal): {tarError}");
                }
            }

            // Copy the tar archive to the container.
            var copyStartInfo = new ProcessStartInfo
            {
                FileName = "docker",
                ArgumentList = { "cp", tarPath, $"{containerId}:/tmp/project.tar" },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using (var copyProcess = Process.Start(copyStartInfo))
            {
                if (copyProcess == null)
                {
                    throw new InvalidOperationException("Failed to start docker cp process");
                }

                await copyProcess.WaitForExitAsync();
                if (copyProcess.ExitCode != 0)
                {
                    var copyError = await copyProcess.StandardError.ReadToEndAsync();
                    throw new InvalidOperationException($"Failed to copy tar to container: {copyError}");
                }
            }

            // Extract the tar archive inside the container.
            await ExecuteInContainerAsync(containerId, "tar", ["-xf", "/tmp/project.tar", "-C", "/workspace"]);

            // Clean up the tar file in the container.
            await ExecuteInContainerAsync(containerId, "rm", ["-f", "/tmp/project.tar"]);

            _logger.Debug($"Project files copied to container successfully");
        }
        finally
        {
            // Clean up temporary tar file on host.
            if (File.Exists(tarPath))
            {
                File.Delete(tarPath);
            }

            if (File.Exists(gitFileListPath))
            {
                File.Delete(gitFileListPath);
            }
        }
    }

    // Uses git to honor .gitignore when possible, with a directory exclusion fallback.
    internal async Task<List<string>> BuildTarArgumentsAsync(string projectRoot, string tarPath, string gitFileListPath)
    {
        var gitPaths = await TryGetGitIncludedPathsAsync(projectRoot);
        if (gitPaths is { Length: > 0 })
        {
            await File.WriteAllTextAsync(gitFileListPath, string.Join('\0', gitPaths) + '\0');

            _logger.Debug("Creating tar archive of project using git ls-files (respects .gitignore)");
            return
            [
                "-cf",
                tarPath,
                "-C",
                projectRoot,
                "--null",
                "-T",
                gitFileListPath
            ];
        }

        // Fall back to static directory exclusions when git metadata is unavailable.
        var tarArgs = new List<string> { "-cf", tarPath };
        foreach (var dir in ExcludedDirectories)
        {
            tarArgs.Add("--exclude");
            tarArgs.Add(dir);
        }

        // Exclude .ando cache contents from copied project files.
        tarArgs.Add("--exclude");
        tarArgs.Add(".ando/cache");
        tarArgs.Add("-C");
        tarArgs.Add(projectRoot);
        tarArgs.Add(".");

        _logger.Debug($"Creating tar archive of project (excluding: {string.Join(", ", ExcludedDirectories)})");
        return tarArgs;
    }

    // Returns tracked + untracked-but-not-ignored paths from git, or null when unavailable.
    internal async Task<string[]?> TryGetGitIncludedPathsAsync(string projectRoot)
    {
        if (!_processRunner.IsAvailable("git"))
        {
            return null;
        }

        // Ensure this directory is in a git work tree.
        var isRepoStartInfo = new ProcessStartInfo
        {
            FileName = "git",
            ArgumentList = { "-C", projectRoot, "rev-parse", "--is-inside-work-tree" },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using (var isRepoProcess = Process.Start(isRepoStartInfo))
        {
            if (isRepoProcess == null)
            {
                return null;
            }

            var isRepoOutput = await isRepoProcess.StandardOutput.ReadToEndAsync();
            await isRepoProcess.WaitForExitAsync();

            if (isRepoProcess.ExitCode != 0 ||
                !string.Equals(isRepoOutput.Trim(), "true", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }
        }

        var listFilesStartInfo = new ProcessStartInfo
        {
            FileName = "git",
            ArgumentList =
            {
                "-C", projectRoot,
                "ls-files",
                "-z",
                "--cached",
                "--others",
                "--exclude-standard",
                "--deduplicate"
            },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        using var listFilesProcess = Process.Start(listFilesStartInfo);
        if (listFilesProcess == null)
        {
            return null;
        }

        var output = await listFilesProcess.StandardOutput.ReadToEndAsync();
        await listFilesProcess.WaitForExitAsync();

        if (listFilesProcess.ExitCode != 0 || string.IsNullOrEmpty(output))
        {
            return null;
        }

        return output
            .Split('\0', StringSplitOptions.RemoveEmptyEntries)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .ToArray();
    }
    // Helper method to execute a command in the container.
    private async Task ExecuteInContainerAsync(string containerId, string command, string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            ArgumentList = { "exec", containerId, command },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException($"Failed to execute {command} in container");
        }

        await process.WaitForExitAsync();
        // Don't throw on non-zero exit - some commands may fail benignly
    }

    /// <summary>
    /// Starts a stopped container.
    /// </summary>
    private async Task StartContainerAsync(string containerId)
    {
        var result = await _processRunner.ExecuteAsync("docker", ["start", containerId]);
        if (!result.Success)
        {
            throw new InvalidOperationException($"Failed to start container: {result.Error}");
        }
    }

    /// <summary>
    /// Stops a running container.
    /// </summary>
    public async Task StopContainerAsync(string containerId)
    {
        await _processRunner.ExecuteAsync("docker", ["stop", containerId]);
    }

    /// <summary>
    /// Removes a container.
    /// </summary>
    public async Task RemoveContainerAsync(string containerName)
    {
        await _processRunner.ExecuteAsync("docker", ["rm", "-f", containerName]);
    }

    /// <summary>
    /// Cleans artifacts directory inside the container.
    /// </summary>
    public async Task CleanArtifactsAsync(string containerId)
    {
        await _processRunner.ExecuteAsync("docker",
            ["exec", containerId, "rm", "-rf", "/workspace/artifacts"]);
        await _processRunner.ExecuteAsync("docker",
            ["exec", containerId, "mkdir", "-p", "/workspace/artifacts"]);
    }
}
