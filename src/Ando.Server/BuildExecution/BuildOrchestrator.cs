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

        // Create linked cancellation token for timeout and manual cancellation
        var timeoutMinutes = Math.Min(project.TimeoutMinutes, _buildSettings.MaxTimeoutMinutes);
        using var timeoutCts = new CancellationTokenSource(TimeSpan.FromMinutes(timeoutMinutes));
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        // Register for cancellation
        _cancellationRegistry.Register(buildId, linkedCts);

        // Create logger for this build
        var buildLogger = new ServerBuildLogger(db, buildId, _hubContext, linkedCts.Token);

        string? containerId = null;
        var repoPath = Path.Combine(_buildSettings.ReposPath, project.Id.ToString(), build.CommitSha[..8]);

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

            buildLogger.Info($"Starting build for {project.RepoFullName}");
            buildLogger.Info($"Branch: {build.Branch}, Commit: {build.ShortCommitSha}");

            // Step 1: Clone or fetch repository
            buildLogger.Info("Preparing repository...");
            var repoReady = await PrepareRepositoryAsync(project, build, repoPath, linkedCts.Token);
            if (!repoReady)
            {
                throw new Exception("Failed to prepare repository");
            }

            // Step 2: Create build container
            buildLogger.Info("Creating build container...");
            containerId = await CreateBuildContainerAsync(project, repoPath, linkedCts.Token);
            if (string.IsNullOrEmpty(containerId))
            {
                throw new Exception("Failed to create build container");
            }

            buildLogger.Info($"Container created: {containerId[..12]}");

            // Step 3: Execute build
            buildLogger.Info("Executing build script...");
            var buildResult = await ExecuteBuildInContainerAsync(
                containerId, project, build, buildLogger, linkedCts.Token);

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
        if (!project.InstallationId.HasValue)
        {
            _logger.LogWarning("No installation ID for project {ProjectId}", project.Id);
            return false;
        }

        if (Directory.Exists(repoPath))
        {
            // Fetch and checkout
            return await _gitHubService.FetchAndCheckoutAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                build.Branch,
                build.CommitSha,
                repoPath);
        }
        else
        {
            // Clone
            Directory.CreateDirectory(Path.GetDirectoryName(repoPath)!);
            return await _gitHubService.CloneRepositoryAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                build.Branch,
                build.CommitSha,
                repoPath);
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
        // Ensure the isolated build network exists
        await EnsureBuildNetworkAsync(cancellationToken);

        var image = project.DockerImage ?? _buildSettings.DefaultDockerImage;

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
        startInfo.ArgumentList.Add("/var/run/docker.sock:/var/run/docker.sock");
        startInfo.ArgumentList.Add("-w");
        startInfo.ArgumentList.Add("/workspace");

        // Add environment variables from secrets (decrypted)
        foreach (var secret in project.Secrets)
        {
            var decryptedValue = _encryption.Decrypt(secret.EncryptedValue);
            startInfo.ArgumentList.Add("-e");
            startInfo.ArgumentList.Add($"{secret.Name}={decryptedValue}");
        }

        startInfo.ArgumentList.Add("--entrypoint");
        startInfo.ArgumentList.Add("tail");
        startInfo.ArgumentList.Add(image);
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add("/dev/null");

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            return null;
        }

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            _logger.LogError("Failed to create container: {Error}", error);
            return null;
        }

        var containerId = (await process.StandardOutput.ReadToEndAsync(cancellationToken)).Trim();
        return containerId;
    }

    /// <summary>
    /// Executes the build inside the container.
    /// </summary>
    private async Task<(bool success, int stepsTotal, int stepsCompleted, int stepsFailed, string? error)>
        ExecuteBuildInContainerAsync(
            string containerId,
            Project project,
            Build build,
            ServerBuildLogger logger,
            CancellationToken cancellationToken)
    {
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
        startInfo.ArgumentList.Add("dotnet");
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add("/ando"); // Ando would be installed in container
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--dind"); // Enable Docker-in-Docker for nested builds

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

        // TODO: Parse step counts from output or get from build log
        return (success, 0, 0, success ? 0 : 1, success ? null : "Build failed");
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
    private static string GetBuildUrl(Build build)
    {
        // TODO: Get base URL from configuration
        return $"/builds/{build.Id}";
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
