// =============================================================================
// BuildOrchestrator.cs
//
// Summary: Core build execution orchestration.
//
// Manages the complete build lifecycle including:
// 1. Container creation with Docker-in-Docker support
// 2. Repository cloning/fetching
// 3. Build script execution via Ando
// 4. Artifact collection
// 5. Status updates and notifications
//
// Design Decisions:
// - Creates fresh DbContext per build to avoid tracking issues
// - Uses timeout from project settings with appsettings fallback
// - Sends email notifications on failure only
// - Always posts GitHub commit status (pending/success/failure)
// =============================================================================

using System.Diagnostics;
using System.Reflection;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Hubs;
using Ando.Server.Models;
using Ando.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.BuildExecution;

/// <summary>
/// Core build execution orchestration.
/// </summary>
public class BuildOrchestrator : IBuildOrchestrator
{
    /// <summary>
    /// Name of the isolated Docker network for build containers.
    /// This network provides internet access but isolates builds from
    /// the host network and other non-build containers.
    /// </summary>
    private const string BuildNetworkName = "ando-builds";

    private readonly IServiceProvider _serviceProvider;
    private readonly IGitHubService _gitHubService;
    private readonly IEmailService _emailService;
    private readonly IEncryptionService _encryption;
    private readonly IProfileDetector _profileDetector;
    private readonly IHubContext<BuildLogHub> _hubContext;
    private readonly CancellationTokenRegistry _cancellationRegistry;
    private readonly BuildSettings _buildSettings;
    private readonly StorageSettings _storageSettings;
    private readonly ILogger<BuildOrchestrator> _logger;

    public BuildOrchestrator(
        IServiceProvider serviceProvider,
        IGitHubService gitHubService,
        IEmailService emailService,
        IEncryptionService encryption,
        IProfileDetector profileDetector,
        IHubContext<BuildLogHub> hubContext,
        CancellationTokenRegistry cancellationRegistry,
        IOptions<BuildSettings> buildSettings,
        IOptions<StorageSettings> storageSettings,
        ILogger<BuildOrchestrator> logger)
    {
        _serviceProvider = serviceProvider;
        _gitHubService = gitHubService;
        _emailService = emailService;
        _encryption = encryption;
        _profileDetector = profileDetector;
        _hubContext = hubContext;
        _cancellationRegistry = cancellationRegistry;
        _buildSettings = buildSettings.Value;
        _storageSettings = storageSettings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task ExecuteBuildAsync(int buildId, CancellationToken cancellationToken)
    {
        // Create scoped DbContext for this build
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AndoDbContext>();

        var build = await db.Builds
            .Include(b => b.Project)
            .ThenInclude(p => p.Secrets)
            .Include(b => b.Project.Owner)
            .FirstOrDefaultAsync(b => b.Id == buildId, cancellationToken);

        if (build == null)
        {
            _logger.LogWarning("Build {BuildId} not found", buildId);
            return;
        }

        var project = build.Project;

        // Validate and update profile configuration
        if (project.InstallationId.HasValue)
        {
            var detectedProfiles = await _profileDetector.DetectProfilesAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                build.Branch);

            // Update available profiles in the project
            project.SetAvailableProfiles(detectedProfiles);
            await db.SaveChangesAsync(cancellationToken);

            // Validate selected profile still exists
            if (!string.IsNullOrWhiteSpace(project.Profile) && !project.IsProfileValid())
            {
                build.Status = BuildStatus.Failed;
                build.ErrorMessage = $"Invalid profile configuration: Profile '{project.Profile}' no longer exists in build.csando. " +
                    $"Available profiles: {(detectedProfiles.Count > 0 ? string.Join(", ", detectedProfiles) : "(none)")}. " +
                    "Please update the project settings to select a valid profile or clear the profile setting.";
                build.FinishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Build {BuildId} failed due to invalid profile '{Profile}' in project {ProjectId}",
                    buildId, project.Profile, project.Id);
                return;
            }
        }

        // Create linked cancellation token for timeout and manual cancellation
        var timeoutMinutes = Math.Min(project.TimeoutMinutes, _buildSettings.MaxTimeoutMinutes);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Register for cancellation
        _cancellationRegistry.Register(buildId, linkedCts);

        // Create logger for this build
        var buildLogger = new ServerBuildLogger(db, buildId, _hubContext, linkedCts.Token);

        string? containerId = null;
        var shortSha = build.CommitSha.Length >= 8 ? build.CommitSha[..8] : build.CommitSha;

        // Two paths needed:
        // - repoPathHost: HOST filesystem path for Docker volume mounts
        // - repoPathServer: Path as seen inside the server container for git operations
        var repoPathHost = Path.Combine(_buildSettings.ReposPath, project.Id.ToString(), shortSha);
        var repoPathServer = Path.Combine(_buildSettings.GetReposPathForServer(), project.Id.ToString(), shortSha);

        try
        {
            // Update status to running
            build.Status = BuildStatus.Running;
            build.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(linkedCts.Token);

            // Post pending status to GitHub
            if (project.InstallationId.HasValue)
            {
                await _gitHubService.SetCommitStatusAsync(
                    project.InstallationId.Value,
                    project.RepoFullName,
                    build.CommitSha,
                    CommitStatusState.Pending,
                    "Build in progress",
                    GetBuildUrl(build));
            }

            buildLogger.Info($"Server Ando version: {GetServerAndoVersion()}");
            buildLogger.Info($"Starting build for {project.RepoFullName}");
            buildLogger.Info($"Branch: {build.Branch}, Commit: {build.ShortCommitSha}");

            // Step 1: Clone or fetch repository (uses server-internal path)
            buildLogger.Info("Preparing repository...");
            var repoReady = await PrepareRepositoryAsync(project, build, repoPathServer, linkedCts.Token);
            if (!repoReady)
            {
                throw new Exception("Failed to prepare repository");
            }

            // Step 2: Create build container (uses host path for Docker volume mount)
            buildLogger.Info("Creating build container...");
            containerId = await CreateBuildContainerAsync(project, repoPathHost, linkedCts.Token);
            if (string.IsNullOrEmpty(containerId))
            {
                throw new Exception("Failed to create build container");
            }

            buildLogger.Info($"Container created: {containerId[..12]}");

            // Step 3: Execute build
            buildLogger.Info("Executing build script...");
            if (!string.IsNullOrWhiteSpace(project.Profile))
            {
                buildLogger.Info($"Using profile: {project.Profile}");
            }
            var buildResult = await ExecuteBuildInContainerAsync(
                containerId, project.Profile, build, buildLogger, linkedCts.Token);

            // Step 4: Collect artifacts
            if (buildResult.success)
            {
                buildLogger.Info("Collecting artifacts...");
                await CollectArtifactsAsync(db, build, containerId, linkedCts.Token);
            }

            // Update final status
            build.Status = buildResult.success ? BuildStatus.Success : BuildStatus.Failed;
            build.StepsTotal = buildResult.stepsTotal;
            build.StepsCompleted = buildResult.stepsCompleted;
            build.StepsFailed = buildResult.stepsFailed;

            if (!buildResult.success)
            {
                build.ErrorMessage = buildResult.error;
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
        {
            build.Status = BuildStatus.TimedOut;
            build.ErrorMessage = $"Build timed out after {timeoutMinutes} minutes";
            buildLogger.Error(build.ErrorMessage);
        }
        catch (OperationCanceledException)
        {
            build.Status = BuildStatus.Cancelled;
            build.ErrorMessage = "Build was cancelled";
            buildLogger.Info(build.ErrorMessage);
        }
        catch (Exception ex)
        {
            build.Status = BuildStatus.Failed;
            build.ErrorMessage = ex.Message;
            buildLogger.Error($"Build failed: {ex.Message}");
            _logger.LogError(ex, "Build {BuildId} failed with exception", buildId);
        }
        finally
        {
            // Finalize build
            build.FinishedAt = DateTime.UtcNow;
            if (build.StartedAt.HasValue)
            {
                build.Duration = build.FinishedAt - build.StartedAt;
            }

            await db.SaveChangesAsync();

            // Unregister cancellation
            _cancellationRegistry.Unregister(buildId);

            // Cleanup container
            if (!string.IsNullOrEmpty(containerId))
            {
                await CleanupContainerAsync(containerId);
            }

            // Post final status to GitHub
            if (project.InstallationId.HasValue)
            {
                var state = build.Status switch
                {
                    BuildStatus.Success => CommitStatusState.Success,
                    BuildStatus.Cancelled => CommitStatusState.Error,
                    _ => CommitStatusState.Failure
                };

                await _gitHubService.SetCommitStatusAsync(
                    project.InstallationId.Value,
                    project.RepoFullName,
                    build.CommitSha,
                    state,
                    GetStatusDescription(build),
                    GetBuildUrl(build));
            }

            // Send failure notification
            if (build.Status == BuildStatus.Failed && project.NotifyOnFailure)
            {
                var email = project.GetNotificationEmail();
                if (!string.IsNullOrEmpty(email))
                {
                    await _emailService.SendBuildFailedEmailAsync(build, email);
                }
            }

            _logger.LogInformation(
                "Build {BuildId} completed with status {Status} in {Duration:F1}s",
                buildId, build.Status, build.Duration?.TotalSeconds ?? 0);
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
                repoPath);
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
                repoPath);
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
        foreach (var secret in project.Secrets)
        {
            var decryptedValue = _encryption.Decrypt(secret.EncryptedValue);
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"{secret.Name}={decryptedValue}");
        }

        // Set ANDO_HOST_ROOT for Docker-in-Docker path mapping.
        // When ando CLI runs with --dind inside this container, it needs to know
        // the actual HOST path (not /workspace) for creating nested container volume mounts.
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add($"ANDO_HOST_ROOT={repoPath}");

        startInfo.ArgumentList.Add("--entrypoint");
        startInfo.ArgumentList.Add("tail");
        startInfo.ArgumentList.Add(image);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("/dev/null");

        // Log the full command for debugging
        var cmdArgs = string.Join(" ", startInfo.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
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

    private async Task<bool> EnsureAndoCliInstalledAsync(
        string containerId,
        ServerBuildLogger logger,
        CancellationToken cancellationToken)
    {
        // Use a fixed tool-path to avoid PATH/global tool issues.
        // Prefer update (fast when already installed), fall back to install.
        var shellCmd = "dotnet tool update --tool-path /tmp/ando-tools ando || dotnet tool install --tool-path /tmp/ando-tools ando";
        var exitCode = await RunDockerExecAsync(
            containerId,
            ["sh", "-c", shellCmd],
            logger,
            cancellationToken);
        return exitCode == 0;
    }

    private async Task<int> RunDockerExecAsync(
        string containerId,
        IReadOnlyList<string> argsAfterContainerId,
        ServerBuildLogger logger,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("exec");
        startInfo.ArgumentList.Add(containerId);
        foreach (var a in argsAfterContainerId)
        {
            startInfo.ArgumentList.Add(a);
        }

        // Log the command for debugging
        var cmdArgs = string.Join(" ", startInfo.ArgumentList.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
        _logger.LogInformation("RunDockerExec: docker {Args}", cmdArgs);

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            logger.Output("Failed to start docker exec process");
            return 1;
        }

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

        return process.ExitCode;
    }

    /// <summary>
    /// Collects build artifacts.
    /// </summary>
    private async Task CollectArtifactsAsync(
        AndoDbContext db,
        Build build,
        string containerId,
        CancellationToken cancellationToken)
    {
        var artifactDir = Path.Combine(_storageSettings.ArtifactsPath, build.ProjectId.ToString(), build.Id.ToString());
        Directory.CreateDirectory(artifactDir);

        // Copy artifacts from container
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        startInfo.ArgumentList.Add("cp");
        startInfo.ArgumentList.Add($"{containerId}:/workspace/artifacts/.");
        startInfo.ArgumentList.Add(artifactDir);

        using var process = Process.Start(startInfo);
        if (process != null)
        {
            await process.WaitForExitAsync(cancellationToken);
        }

        // Record artifacts in database
        if (Directory.Exists(artifactDir))
        {
            var retentionDays = _storageSettings.ArtifactRetentionDays;
            var expiresAt = DateTime.UtcNow.AddDays(retentionDays);

            foreach (var file in Directory.GetFiles(artifactDir, "*", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                var relativePath = Path.GetRelativePath(artifactDir, file);

                db.BuildArtifacts.Add(new BuildArtifact
                {
                    BuildId = build.Id,
                    Name = Path.GetFileName(file),
                    StoragePath = $"{build.ProjectId}/{build.Id}/{relativePath}",
                    SizeBytes = info.Length,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt
                });
            }

            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Cleans up the build container.
    /// </summary>
    private async Task CleanupContainerAsync(string containerId)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "docker",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };

            startInfo.ArgumentList.Add("rm");
            startInfo.ArgumentList.Add("-f");
            startInfo.ArgumentList.Add(containerId);

            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup container {ContainerId}", containerId);
        }
    }

    /// <summary>
    /// Gets the URL for viewing a build.
    /// </summary>
    private string? GetBuildUrl(Build build)
    {
        if (string.IsNullOrEmpty(_buildSettings.BaseUrl))
        {
            return null;
        }
        return $"{_buildSettings.BaseUrl.TrimEnd('/')}/builds/{build.Id}";
    }

    /// <summary>
    /// Gets a description for the build status.
    /// </summary>
    private static string GetStatusDescription(Build build) => build.Status switch
    {
        BuildStatus.Success => $"Build succeeded in {build.Duration?.TotalSeconds:F0}s",
        BuildStatus.Failed => build.ErrorMessage ?? "Build failed",
        BuildStatus.Cancelled => "Build was cancelled",
        BuildStatus.TimedOut => $"Build timed out after {build.Duration?.TotalMinutes:F0}m",
        _ => "Unknown status"
    };
}
