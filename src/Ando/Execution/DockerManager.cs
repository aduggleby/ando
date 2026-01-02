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
// - Creates containers with the project mounted at /workspace
// - Mounts cache directories to persist NuGet/npm packages across builds
// - Uses 'tail -f /dev/null' to keep containers running indefinitely
// - ContainerExecutor handles command execution within containers
//
// Design Decisions:
// - Warm containers avoid the overhead of container creation on each build
// - Alpine-based images for smaller size and faster pulls
// - Cache directories persist across builds for faster dependency resolution
// - Project is mounted read-write for build outputs
// - Optional Docker socket mount enables Docker-in-Docker for image builds
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
    /// Project root directory to mount at /workspace in the container.
    /// </summary>
    public required string ProjectRoot { get; set; }

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
                return existing with { IsRunning = true };
            }
            _logger.Debug($"Reusing running container: {config.Name}");
            return existing;
        }

        _logger.Debug($"Creating new container: {config.Name}");
        return await CreateContainerAsync(config);
    }

    // Creates a new container with appropriate mounts and configuration.
    // Container runs 'tail -f /dev/null' to stay alive indefinitely.
    private async Task<ContainerInfo> CreateContainerAsync(ContainerConfig config)
    {
        _logger.Info($"Creating Docker container '{config.Name}'");
        _logger.Info($"  Image: {config.Image}");
        _logger.Info($"  Mount: {config.ProjectRoot} -> /workspace");

        // Build the docker run command arguments.
        var args = new List<string>
        {
            "run",
            "-d",                              // Detached mode
            "--name", config.Name,             // Named for easy lookup
            "-v", $"{config.ProjectRoot}:/workspace",  // Mount project
            "-w", "/workspace",                // Set working directory
            "--entrypoint", "tail",            // Override entrypoint to keep alive
        };

        // Ensure cache directories exist before mounting.
        // These persist NuGet packages and npm modules across builds.
        var cacheDir = Path.Combine(config.ProjectRoot, ".ando", "cache");
        var nugetCacheDir = Path.Combine(cacheDir, "nuget");
        var npmCacheDir = Path.Combine(cacheDir, "npm");
        Directory.CreateDirectory(nugetCacheDir);
        Directory.CreateDirectory(npmCacheDir);

        _logger.Debug($"  Created host cache directory: {nugetCacheDir}");
        _logger.Debug($"  Created host cache directory: {npmCacheDir}");

        // Configure environment variables for package manager caches.
        // This significantly speeds up subsequent builds.
        args.AddRange(new[]
        {
            "-e", "NUGET_PACKAGES=/workspace/.ando/cache/nuget",
            "-e", "npm_config_cache=/workspace/.ando/cache/npm",
        });

        _logger.Info($"  Container directories:");
        _logger.Info($"    /workspace                    - Project root (mounted)");
        _logger.Info($"    /workspace/artifacts          - Build outputs");
        _logger.Info($"    /workspace/.ando/cache/nuget  - NuGet package cache");
        _logger.Info($"    /workspace/.ando/cache/npm    - npm package cache");

        // Optional: Mount Docker socket for Docker-in-Docker scenarios.
        // Required when builds need to create Docker images.
        if (config.MountDockerSocket)
        {
            args.AddRange(new[] { "-v", "/var/run/docker.sock:/var/run/docker.sock" });
            _logger.Info($"  Docker socket mounted for Docker-in-Docker");
        }

        // Add image and arguments to 'tail' command.
        args.Add(config.Image);
        args.AddRange(new[] { "-f", "/dev/null" }); // Keep container running forever

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

        return new ContainerInfo(containerId, config.Name, true);
    }

    /// <summary>
    /// Starts a stopped container.
    /// </summary>
    private async Task StartContainerAsync(string containerId)
    {
        var result = await _processRunner.ExecuteAsync("docker", new[] { "start", containerId });
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
        await _processRunner.ExecuteAsync("docker", new[] { "stop", containerId });
    }

    /// <summary>
    /// Removes a container.
    /// </summary>
    public async Task RemoveContainerAsync(string containerName)
    {
        await _processRunner.ExecuteAsync("docker", new[] { "rm", "-f", containerName });
    }

    /// <summary>
    /// Cleans artifacts directory inside the container.
    /// </summary>
    public async Task CleanArtifactsAsync(string containerId)
    {
        await _processRunner.ExecuteAsync("docker", new[]
        {
            "exec", containerId, "rm", "-rf", "/workspace/artifacts"
        });
        await _processRunner.ExecuteAsync("docker", new[]
        {
            "exec", containerId, "mkdir", "-p", "/workspace/artifacts"
        });
    }
}
