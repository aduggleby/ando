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
public partial class BuildOrchestrator : IBuildOrchestrator
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

            // Validate the build's profile still exists
            if (!string.IsNullOrWhiteSpace(build.Profile) &&
                !detectedProfiles.Contains(build.Profile, StringComparer.OrdinalIgnoreCase))
            {
                build.Status = BuildStatus.Failed;
                build.ErrorMessage = $"Invalid profile configuration: Profile '{build.Profile}' no longer exists in build.csando. " +
                    $"Available profiles: {(detectedProfiles.Count > 0 ? string.Join(", ", detectedProfiles) : "(none)")}. " +
                    "Please update the project settings to select a valid profile or clear the profile setting.";
                build.FinishedAt = DateTime.UtcNow;
                await db.SaveChangesAsync(cancellationToken);

                _logger.LogWarning(
                    "Build {BuildId} failed due to invalid profile '{Profile}' in project {ProjectId}",
                    buildId, build.Profile, project.Id);
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
        // - repoPathHost: HOST filesystem path for Docker volume mounts (used by the Docker daemon)
        // - repoPathServer: Path as seen inside the server container for git operations
        //
        // When the server runs in a container, the path inside the container (e.g. /data/repos)
        // is not necessarily a valid host path. If ReposPathInContainer isn't configured,
        // we resolve the host path by inspecting this server container's bind mounts.
        string repoPathServer;
        string repoPathHost;
        if (!string.IsNullOrWhiteSpace(_buildSettings.ReposPathInContainer))
        {
            repoPathServer = Path.Combine(_buildSettings.ReposPathInContainer!, project.Id.ToString(), shortSha);
            repoPathHost = Path.Combine(_buildSettings.ReposPath, project.Id.ToString(), shortSha);
        }
        else
        {
            repoPathServer = Path.Combine(_buildSettings.ReposPath, project.Id.ToString(), shortSha);
            repoPathHost = await ResolveHostPathForServerPathAsync(repoPathServer, linkedCts.Token);
        }

        // Provide a GitHub token to the build container for operations that need it (ghcr push, releases, etc.).
        // Prefer an explicitly-configured secret; otherwise fall back to the GitHub App installation token.
        string? githubTokenForBuild = null;
        if (project.InstallationId.HasValue)
        {
            try
            {
                githubTokenForBuild = await _gitHubService.GetInstallationTokenAsync(project.InstallationId.Value);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get installation token for project {ProjectId}", project.Id);
            }
        }

        try
        {
            // Update status to running
            build.Status = BuildStatus.Running;
            build.StartedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(linkedCts.Token);
            await PublishBuildStatusChangedAsync(build, project.OwnerId, linkedCts.Token);

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
            buildLogger.Info($"Repo path (server): {repoPathServer}");
            buildLogger.Info($"Repo path (host): {repoPathHost}");

            // Step 1: Clone or fetch repository (uses server-internal path)
            buildLogger.Info("Preparing repository...");
            var repoReady = await PrepareRepositoryAsync(project, build, repoPathServer, linkedCts.Token);
            if (!repoReady)
            {
                throw new Exception("Failed to prepare repository");
            }

            // Step 2: Create build container (uses host path for Docker volume mount)
            buildLogger.Info("Creating build container...");
            containerId = await CreateBuildContainerAsync(project, repoPathHost, githubTokenForBuild, linkedCts.Token);
            if (string.IsNullOrEmpty(containerId))
            {
                throw new Exception("Failed to create build container");
            }

            buildLogger.Info($"Container created: {containerId[..12]}");

            // Step 3: Execute build
            buildLogger.Info("Executing build script...");
            if (!string.IsNullOrWhiteSpace(build.Profile))
            {
                buildLogger.Info($"Using profile: {build.Profile}");
            }
            var buildResult = await ExecuteBuildInContainerAsync(
                containerId, build.Profile, build, buildLogger, linkedCts.Token);

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
            build.GitVersionTag = await TryResolveGitVersionTagAsync(repoPathServer, build.CommitSha);
            build.FinishedAt = DateTime.UtcNow;
            if (build.StartedAt.HasValue)
            {
                build.Duration = build.FinishedAt - build.StartedAt;
            }

            await db.SaveChangesAsync();
            await PublishBuildStatusChangedAsync(build, project.OwnerId, CancellationToken.None);
            await _hubContext.Clients
                .Group(BuildLogHub.GetGroupName(build.Id))
                .SendAsync("BuildCompleted", new
                {
                    buildId = build.Id,
                    status = build.Status.ToString(),
                    finishedAt = build.FinishedAt,
                    durationSeconds = build.Duration?.TotalSeconds
                });

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
                "Build {BuildId} completed with status {Status} in {Duration:F1}s (gitTag: {GitTag})",
                buildId, build.Status, build.Duration?.TotalSeconds ?? 0, build.GitVersionTag ?? "-");
        }
    }

    private async Task PublishBuildStatusChangedAsync(Build build, int ownerId, CancellationToken ct)
    {
        await _hubContext.Clients
            .Group(BuildLogHub.GetUserGroupName(ownerId))
            .SendAsync("BuildStatusChanged", new
            {
                buildId = build.Id,
                projectId = build.ProjectId,
                status = build.Status.ToString(),
                queuedAt = build.QueuedAt,
                startedAt = build.StartedAt,
                finishedAt = build.FinishedAt,
                durationSeconds = build.Duration?.TotalSeconds,
                gitVersionTag = build.GitVersionTag,
                errorMessage = build.ErrorMessage
            }, ct);
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
