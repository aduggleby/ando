// =============================================================================
// BuildOrchestrator.RepositoryAndPaths.cs
//
// Summary: Repository preparation and host/container path resolution helpers
// for build orchestration.
//
// These helpers are separated from the main orchestration flow to keep
// ExecuteBuildAsync focused on lifecycle coordination.
// =============================================================================

using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Ando.Server.Data;
using Ando.Server.Models;

namespace Ando.Server.BuildExecution;

public partial class BuildOrchestrator
{
    private async Task<string?> TryResolveGitVersionTagAsync(string repoPath, string commitSha)
    {
        if (string.IsNullOrWhiteSpace(repoPath) || !Directory.Exists(repoPath))
        {
            return null;
        }

        var target = string.IsNullOrWhiteSpace(commitSha) ? "HEAD" : commitSha;

        // Priority 1: Exact tag that points to this commit.
        var exactTag = await RunGitAndReadFirstLineAsync(
            repoPath,
            "tag",
            "--points-at",
            target,
            "--sort=-version:refname");

        if (!string.IsNullOrWhiteSpace(exactTag))
        {
            return exactTag.Length > 100 ? exactTag[..100] : exactTag;
        }

        // Priority 2: Nearest reachable tag from this commit.
        var nearestTag = await RunGitAndReadFirstLineAsync(
            repoPath,
            "describe",
            "--tags",
            "--abbrev=0",
            target);

        if (!string.IsNullOrWhiteSpace(nearestTag))
        {
            return nearestTag.Length > 100 ? nearestTag[..100] : nearestTag;
        }

        return null;
    }

    private async Task<string?> RunGitAndReadFirstLineAsync(string repoPath, params string[] args)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(repoPath);
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return null;
            }

            await process.WaitForExitAsync();
            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogDebug(
                    "Failed running git command for repoPath={RepoPath}. args=[{Args}] error={Error}",
                    repoPath,
                    string.Join(' ', args),
                    error);
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var tag = output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .FirstOrDefault();

            if (string.IsNullOrWhiteSpace(tag))
            {
                return null;
            }

            return tag;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Error while running git command for repoPath={RepoPath}. args=[{Args}]",
                repoPath,
                string.Join(' ', args));
            return null;
        }
    }

    /// <summary>
    /// Prepares the repository by cloning or fetching.
    /// </summary>
    private async Task<bool> PrepareRepositoryAsync(
        Project project,
        Build build,
        string repoPath,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("PrepareRepository: Starting for path {RepoPath}", repoPath);

        if (!project.InstallationId.HasValue)
        {
            _logger.LogWarning("No installation ID for project {ProjectId}", project.Id);
            return false;
        }

        _logger.LogInformation("PrepareRepository: InstallationId={InstallationId}", project.InstallationId.Value);

        // If the user has configured a GITHUB_TOKEN secret, prefer it for git clone/fetch/push operations.
        // The GitHub App installation token used by the server may not have write permissions for all repos,
        // which breaks publish profiles that include Git.Push/Git.PushTags.
        string? gitTokenOverride = null;
        try
        {
            var gitHubTokenSecret = project.Secrets.FirstOrDefault(s =>
                string.Equals(s.Name, "GITHUB_TOKEN", StringComparison.OrdinalIgnoreCase));
            if (gitHubTokenSecret != null)
            {
                gitTokenOverride = _encryption.Decrypt(gitHubTokenSecret.EncryptedValue);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "PrepareRepository: Failed to decrypt GITHUB_TOKEN for project {ProjectId}", project.Id);
        }

        var dirExists = Directory.Exists(repoPath);
        _logger.LogInformation("PrepareRepository: Directory exists={DirExists}", dirExists);

        if (dirExists)
        {
            // Fetch and checkout
            _logger.LogInformation("PrepareRepository: Calling FetchAndCheckout");
            var result = await _gitHubService.FetchAndCheckoutAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                build.Branch,
                build.CommitSha,
                repoPath,
                gitTokenOverride);
            _logger.LogInformation("PrepareRepository: FetchAndCheckout returned {Result}", result);
            return result;
        }
        else
        {
            // Clone
            _logger.LogInformation("PrepareRepository: Calling Clone");
            var parentDir = Path.GetDirectoryName(repoPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }
            var result = await _gitHubService.CloneRepositoryAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                build.Branch,
                build.CommitSha,
                repoPath,
                gitTokenOverride);
            _logger.LogInformation("PrepareRepository: Clone returned {Result}", result);
            return result;
        }
    }

    /// <summary>
    /// Ensures the isolated build network exists.
    /// Creates it if it doesn't exist.
    /// </summary>
    private async Task EnsureBuildNetworkAsync(CancellationToken cancellationToken)
    {
        // Check if network already exists
        var checkInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        checkInfo.ArgumentList.Add("network");
        checkInfo.ArgumentList.Add("inspect");
        checkInfo.ArgumentList.Add(BuildNetworkName);

        using var checkProcess = Process.Start(checkInfo);
        if (checkProcess != null)
        {
            await checkProcess.WaitForExitAsync(cancellationToken);
            if (checkProcess.ExitCode == 0)
            {
                // Network already exists
                return;
            }
        }

        // Create the network
        _logger.LogInformation("Creating isolated build network: {NetworkName}", BuildNetworkName);

        var createInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        createInfo.ArgumentList.Add("network");
        createInfo.ArgumentList.Add("create");
        createInfo.ArgumentList.Add("--driver");
        createInfo.ArgumentList.Add("bridge");
        createInfo.ArgumentList.Add(BuildNetworkName);

        using var createProcess = Process.Start(createInfo);
        if (createProcess != null)
        {
            await createProcess.WaitForExitAsync(cancellationToken);
            if (createProcess.ExitCode != 0)
            {
                var error = await createProcess.StandardError.ReadToEndAsync(cancellationToken);
                _logger.LogWarning("Failed to create build network: {Error}", error);
            }
        }
    }

    /// <summary>
    /// Creates a Docker container for the build.
    /// </summary>
    private async Task<string?> CreateBuildContainerAsync(
        Project project,
        string repoPath,
        string? githubToken,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("CreateBuildContainer: Starting for repo path {RepoPath}", repoPath);

        // Ensure the isolated build network exists
        await EnsureBuildNetworkAsync(cancellationToken);

        var image = project.DockerImage ?? _buildSettings.DefaultDockerImage;
        _logger.LogInformation("CreateBuildContainer: Using image {Image}", image);

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("-d");
        startInfo.ArgumentList.Add("--rm");

        // Use isolated build network (provides internet access but isolates from host/other containers)
        startInfo.ArgumentList.Add("--network");
        startInfo.ArgumentList.Add(BuildNetworkName);

        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add($"{repoPath}:/workspace");
        startInfo.ArgumentList.Add("-v");
        startInfo.ArgumentList.Add($"{_buildSettings.DockerSocketPath}:/var/run/docker.sock");
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("/workspace");

        // Add environment variables from secrets (decrypted)
        var hasGitHubTokenSecret = false;
        foreach (var secret in project.Secrets)
        {
            var decryptedValue = _encryption.Decrypt(secret.EncryptedValue);
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"{secret.Name}={decryptedValue}");
            if (string.Equals(secret.Name, "GITHUB_TOKEN", StringComparison.OrdinalIgnoreCase))
            {
                hasGitHubTokenSecret = true;
            }
        }

        if (hasGitHubTokenSecret)
        {
            _logger.LogInformation(
                "CreateBuildContainer: Using project secret GITHUB_TOKEN for GitHub operations (git, releases, ghcr).");
        }

        // If the project hasn't explicitly configured a GitHub token secret, provide the GitHub App
        // installation token. This enables ghcr pushes and GitHub release operations in build scripts.
        if (!hasGitHubTokenSecret && !string.IsNullOrWhiteSpace(githubToken))
        {
            _logger.LogWarning(
                "CreateBuildContainer: No GITHUB_TOKEN secret configured; injecting GitHub App installation token. " +
                "If ghcr.io pushes fail with permission errors, configure a project secret named GITHUB_TOKEN using a PAT with write:packages.");

            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"GITHUB_TOKEN={githubToken}");
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"GITHUB_REPOSITORY={project.RepoFullName}");
        }

        // ANDO_HOST_ROOT is used by the Ando CLI to translate "/workspace/..." paths for steps that
        // run on the build container (like Ando.Build spawning nested builds).
        //
        // On Ando.Server we bind-mount the repo to /workspace, so using "/workspace" here ensures:
        // - child builds can find and read build scripts (paths exist in this container)
        // - nested builds copy from the container filesystem (docker cp/tar), so they do NOT need
        //   access to the physical host path for volume mounts.
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add("ANDO_HOST_ROOT=/workspace");

        startInfo.ArgumentList.Add("--entrypoint");
        startInfo.ArgumentList.Add("tail");
        startInfo.ArgumentList.Add(image);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("/dev/null");

        // Log a redacted version of the command (do not leak secrets).
        var redactedArgs = new List<string>();
        for (var i = 0; i < startInfo.ArgumentList.Count; i++)
        {
            var a = startInfo.ArgumentList[i];
            if (a == "-e" && i + 1 < startInfo.ArgumentList.Count)
            {
                var kv = startInfo.ArgumentList[i + 1];
                var eq = kv.IndexOf('=');
                var key = eq > 0 ? kv[..eq] : kv;
                redactedArgs.Add("-e");
                redactedArgs.Add($"{key}=REDACTED");
                i++; // skip value arg (already redacted)
                continue;
            }

            redactedArgs.Add(a);
        }
        var cmdArgs = string.Join(" ", redactedArgs.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        _logger.LogInformation("CreateBuildContainer: Running docker {Args}", cmdArgs);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            _logger.LogError("CreateBuildContainer: Failed to start docker process");
            return null;
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            _logger.LogError("CreateBuildContainer: Failed (exit code {ExitCode}): {Error}", process.ExitCode, error);
            return null;
        }

        var containerId = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        _logger.LogInformation("CreateBuildContainer: Success, container ID={ContainerId}", containerId);
        return containerId;
    }

    /// <summary>
    /// Executes the build inside the container.
    /// </summary>
    private async Task<(bool success, int stepsTotal, int stepsCompleted, int stepsFailed, string? error)>
        ExecuteBuildInContainerAsync(
            string containerId,
            string? profile,
            Build build,
            ServerBuildLogger logger,
            CancellationToken cancellationToken)
    {
        // Ensure the Ando CLI exists inside the build container.
        // The build container image is user-configurable and typically is a plain .NET SDK image.
        // Install/update the latest Ando .NET tool on every build for correctness over caching.
        logger.Info("Installing Ando CLI (latest)...");
        var toolOk = await EnsureAndoCliInstalledAsync(containerId, logger, cancellationToken);
        if (!toolOk)
        {
            return (false, 0, 0, 1, "Failed to install Ando CLI in build container");
        }

        // The build runs with `--dind`, which relies on the Docker CLI inside the build container
        // (the daemon is provided by mounting the host's /var/run/docker.sock).
        logger.Info("Ensuring Docker CLI is installed in build container...");
        var dockerOk = await EnsureDockerCliInstalledAsync(containerId, logger, cancellationToken);
        if (!dockerOk)
        {
            return (false, 0, 0, 1, "Docker CLI is required in the build container (docker.sock alone is not enough)");
        }

        logger.Info("Ensuring git is installed in build container...");
        var gitOk = await EnsureGitInstalledAsync(containerId, logger, cancellationToken);
        if (!gitOk)
        {
            return (false, 0, 0, 1, "git is required in the build container");
        }

        // Configure git credentials (without embedding tokens in remote URLs).
        // Build scripts may call `git remote get-url origin`; never persist credentials in origin URL.
        logger.Info("Configuring git credentials in build container (if GITHUB_TOKEN is set)...");
        var credsOk = await ConfigureGitCredentialsAsync(containerId, logger, cancellationToken);
        if (!credsOk)
        {
            return (false, 0, 0, 1, "Failed to configure git credentials in build container");
        }

        // Publish profiles commonly create GitHub releases via `gh` CLI (GitHubOperations).
        if (string.Equals(profile, "publish", StringComparison.OrdinalIgnoreCase))
        {
            logger.Info("Ensuring GitHub CLI (gh) is installed in build container...");
            var ghOk = await EnsureGitHubCliInstalledAsync(containerId, logger, cancellationToken);
            if (!ghOk)
            {
                return (false, 0, 0, 1, "GitHub CLI (gh) is required for publish profile GitHub operations");
            }
        }

        // Log the installed version for debugging.
        await RunDockerExecAsync(
            containerId,
            ["/tmp/ando-tools/ando", "--version"],
            logger,
            cancellationToken);

        // Execute ando build inside container
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("exec");
        // docker exec does not reliably inherit the container's working directory.
        // Running from /workspace ensures ando can find build.csando in the repo root.
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("/workspace");
        startInfo.ArgumentList.Add(containerId);
        startInfo.ArgumentList.Add("/tmp/ando-tools/ando");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--dind"); // Enable Docker-in-Docker for nested builds
        startInfo.ArgumentList.Add("--read-env"); // Auto-load .env files without prompting (non-interactive)

        // Add profile flag if a profile is selected
        if (!string.IsNullOrWhiteSpace(profile))
        {
            startInfo.ArgumentList.Add("-p");
            startInfo.ArgumentList.Add(profile);
        }

        // Log the command for debugging
        var cmdArgs = string.Join(" ", startInfo.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        _logger.LogInformation("ExecuteBuild: Running docker {Args}", cmdArgs);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return (false, 0, 0, 0, "Failed to start build process");
        }

        // Stream output
        var outputTask = Task.Run(async () =>
        {
            while (!process.StandardOutput.EndOfStream)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line != null)
                {
                    logger.Output(line);
                }
            }
        }, cancellationToken);

        var errorTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line != null)
                {
                    logger.Output(line);
                }
            }
        }, cancellationToken);

        await process.WaitForExitAsync(cancellationToken);
        await Task.WhenAll(outputTask, errorTask);

        var success = process.ExitCode == 0;
        _logger.LogInformation("ExecuteBuild: Completed with exit code {ExitCode}", process.ExitCode);

        // TODO: Parse step counts from output or get from build log
        return (success, 0, 0, success ? 0 : 1, success ? null : "Build failed");
    }

    private static string GetServerAndoVersion()
    {
        // The server references the Ando project, so this reflects the Ando assembly version
        // that the server is running with (not the build container's tool version).
        var asm = typeof(Ando.Logging.LogLevel).Assembly;
        var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            return info;
        }

        return asm.GetName().Version?.ToString() ?? "unknown";
    }

    private static bool IsRunningInContainer()
    {
        if (Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true")
        {
            return true;
        }

        // Common docker indicator file.
        return File.Exists("/.dockerenv");
    }

    private sealed class DockerMount
    {
        public string? Source { get; set; }
        public string? Destination { get; set; }
    }

    private static bool IsLikelyContainerReference(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        // Docker IDs are hex; names can include dashes/underscores.
        return value.All(char.IsLetterOrDigit) || value.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
    }

    private static string? TryGetContainerIdFromCGroup()
    {
        try
        {
            foreach (var line in File.ReadLines("/proc/self/cgroup"))
            {
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                // cgroup v1/v2 lines often end with a docker container id segment.
                var path = line.Split(':').LastOrDefault()?.Trim();
                if (string.IsNullOrWhiteSpace(path))
                {
                    continue;
                }

                var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
                for (var i = segments.Length - 1; i >= 0; i--)
                {
                    var segment = segments[i].Trim();
                    if (segment.Length >= 12 && segment.All(Uri.IsHexDigit))
                    {
                        return segment;
                    }
                }
            }
        }
        catch
        {
            // Best-effort lookup only.
        }

        return null;
    }

    private static DockerMount? FindBestMountMapping(string serverPath, IEnumerable<DockerMount> mounts)
    {
        DockerMount? best = null;
        var bestLen = -1;
        foreach (var m in mounts)
        {
            if (string.IsNullOrWhiteSpace(m.Destination) || string.IsNullOrWhiteSpace(m.Source))
            {
                continue;
            }

            var dest = m.Destination!.TrimEnd('/');
            if (dest.Length == 0)
            {
                continue;
            }

            if (serverPath == dest || serverPath.StartsWith(dest + "/", StringComparison.Ordinal))
            {
                if (dest.Length > bestLen)
                {
                    best = m;
                    bestLen = dest.Length;
                }
            }
        }

        return best;
    }

    private async Task<List<DockerMount>?> TryInspectContainerMountsAsync(
        string containerRef,
        CancellationToken cancellationToken)
    {
        if (!IsLikelyContainerReference(containerRef))
        {
            return null;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("inspect");
        startInfo.ArgumentList.Add(containerRef);
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{json .Mounts}}");

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return null;
        }

        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var err = await stderrTask;
            _logger.LogDebug(
                "docker inspect failed for container ref {ContainerRef} while resolving host path: {Error}",
                containerRef,
                err);
            return null;
        }

        var mountsJson = (await stdoutTask).Trim();
        if (string.IsNullOrWhiteSpace(mountsJson))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<DockerMount>>(mountsJson);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "Failed to parse mount json for container ref {ContainerRef} while resolving host path",
                containerRef);
            return null;
        }
    }

    private async Task<string> ResolveHostPathForServerPathAsync(string serverPath, CancellationToken cancellationToken)
    {
        // If the server isn't containerized, "serverPath" is already a host path.
        if (!IsRunningInContainer())
        {
            return serverPath;
        }

        // Resolve self container via multiple references because HOSTNAME can be stale after container recreation.
        var candidates = new List<string>();
        var cgroupId = TryGetContainerIdFromCGroup();
        if (!string.IsNullOrWhiteSpace(cgroupId))
        {
            candidates.Add(cgroupId);
        }

        var hostName = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrWhiteSpace(hostName))
        {
            candidates.Add(hostName);
        }

        // Common compose service/container name fallback.
        candidates.Add("ando-server");

        foreach (var candidate in candidates.Distinct(StringComparer.Ordinal))
        {
            var mounts = await TryInspectContainerMountsAsync(candidate, cancellationToken);
            if (mounts == null || mounts.Count == 0)
            {
                continue;
            }

            var best = FindBestMountMapping(serverPath, mounts);
            if (best == null)
            {
                continue;
            }

            var destination = best.Destination!.TrimEnd('/');
            var source = best.Source!.TrimEnd('/');
            var rel = serverPath.Length == destination.Length ? "" : serverPath.Substring(destination.Length);
            return source + rel;
        }

        _logger.LogWarning(
            "Unable to resolve host path mapping for {ServerPath}; falling back to server path. " +
            "Configure Build.ReposPath (host) and Build.ReposPathInContainer (container) to avoid ambiguity.",
            serverPath);
        return serverPath;
    }
}
