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
// - Mounts cache directories to persist NuGet/npm packages across builds
// - Uses 'tail -f /dev/null' to keep containers running indefinitely
// - ContainerExecutor handles command execution within containers
//
// Design Decisions:
// - Project files are copied in (not mounted) for true isolation - Docker
//   operations cannot modify host files during build execution
// - Warm containers avoid the overhead of container creation on each build
// - Alpine-based images for smaller size and faster pulls
// - Cache directories mounted for faster dependency resolution across builds
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
    /// Docker image to use. Default is the .NET SDK alpine image.
    /// </summary>
    public string Image { get; set; } = "mcr.microsoft.com/dotnet/sdk:9.0-alpine";

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
    /// Checks if Docker is available on the system.
    /// </summary>
    public bool IsDockerAvailable()
    {
        return _processRunner.IsAvailable("docker");
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
    /// </summary>
    public async Task<ContainerInfo> EnsureContainerAsync(ContainerConfig config)
    {
        var existing = await FindWarmContainerAsync(config.Name);

        if (existing != null)
        {
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

    // Creates a new container with cache mounts (but NOT project mount).
    // Project files are copied in separately for isolation.
    // Container runs 'tail -f /dev/null' to stay alive indefinitely.
    private async Task<ContainerInfo> CreateContainerAsync(ContainerConfig config)
    {
        _logger.Info($"Creating Docker container '{config.Name}'");
        _logger.Info($"  Image: {config.Image}");
        _logger.Info($"  Project files will be copied (isolated mode)");

        // Ensure cache directories exist on local filesystem.
        // Use local path for directory operations since we're inside the current process/container.
        var localRoot = config.GetLocalProjectRoot();
        var localCacheDir = Path.Combine(localRoot, ".ando", "cache");
        var localNugetCacheDir = Path.Combine(localCacheDir, "nuget");
        var localNpmCacheDir = Path.Combine(localCacheDir, "npm");
        Directory.CreateDirectory(localNugetCacheDir);
        Directory.CreateDirectory(localNpmCacheDir);

        // For Docker volume mounts, use the HOST path (ProjectRoot) which Docker can access.
        // In DinD mode, this differs from the local path used above for directory creation.
        var hostCacheDir = Path.Combine(config.ProjectRoot, ".ando", "cache");
        var hostNugetCacheDir = Path.Combine(hostCacheDir, "nuget");
        var hostNpmCacheDir = Path.Combine(hostCacheDir, "npm");

        _logger.Debug($"  Local cache directory: {localNugetCacheDir}");
        _logger.Debug($"  Host cache directory for mount: {hostNugetCacheDir}");

        // Build the docker run command arguments.
        // Note: Project root is NOT mounted - files are copied in for isolation.
        var args = new List<string>
        {
            "run",
            "-d",                              // Detached mode
            "--name", config.Name,             // Named for easy lookup
            "-w", "/workspace",                // Set working directory
            "--entrypoint", "tail",            // Override entrypoint to keep alive
            // Mount only cache directories for warm container performance.
            // These persist NuGet packages and npm modules across builds.
            // Use HOST paths for volume mounts (Docker needs actual host paths).
            "-v", $"{hostNugetCacheDir}:/workspace/.ando/cache/nuget",
            "-v", $"{hostNpmCacheDir}:/workspace/.ando/cache/npm",
            // Configure environment variables for package manager caches.
            "-e", "NUGET_PACKAGES=/workspace/.ando/cache/nuget",
            "-e", "npm_config_cache=/workspace/.ando/cache/npm",
        };

        _logger.Info($"  Container directories:");
        _logger.Info($"    /workspace                    - Project root (copied, isolated)");
        _logger.Info($"    /workspace/artifacts          - Build outputs");
        _logger.Info($"    /workspace/.ando/cache/nuget  - NuGet package cache (mounted)");
        _logger.Info($"    /workspace/.ando/cache/npm    - npm package cache (mounted)");

        // Optional: Mount Docker socket for Docker-in-Docker scenarios.
        // Required when builds need to create Docker images.
        if (config.MountDockerSocket)
        {
            args.AddRange(["-v", "/var/run/docker.sock:/var/run/docker.sock"]);
            _logger.Info($"  Docker socket mounted for Docker-in-Docker");
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

        var containerId = (await process.StandardOutput.ReadToEndAsync()).Trim();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

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
        // First, clean the workspace (except cache directories).
        // This ensures a fresh state for each build.
        await ExecuteInContainerAsync(containerId, "sh", ["-c",
            "find /workspace -mindepth 1 -maxdepth 1 ! -name '.ando' -exec rm -rf {} +"]);

        // Create a temporary tar archive excluding unwanted directories.
        // Using tar is much faster than individual docker cp calls.
        var tarPath = Path.Combine(Path.GetTempPath(), $"ando-project-{Guid.NewGuid():N}.tar");

        try
        {
            // Build tar exclude arguments.
            var excludeArgs = new List<string> { "-cf", tarPath };
            foreach (var dir in ExcludedDirectories)
            {
                excludeArgs.Add("--exclude");
                excludeArgs.Add(dir);
            }
            // Exclude .ando directory except we want it for structure, just not cache contents
            excludeArgs.Add("--exclude");
            excludeArgs.Add(".ando/cache");

            excludeArgs.Add("-C");
            excludeArgs.Add(projectRoot);
            excludeArgs.Add(".");

            // Create the tar archive on the host.
            var tarStartInfo = new ProcessStartInfo
            {
                FileName = "tar",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            foreach (var arg in excludeArgs)
            {
                tarStartInfo.ArgumentList.Add(arg);
            }

            _logger.Debug($"Creating tar archive of project (excluding: {string.Join(", ", ExcludedDirectories)})");

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
        }
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
