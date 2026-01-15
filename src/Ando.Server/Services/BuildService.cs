// =============================================================================
// BuildService.cs
//
// Summary: Implementation of build management operations.
//
// Handles build lifecycle management including queuing, status updates,
// cancellation, and retry. Uses Hangfire for background job scheduling.
//
// Design Decisions:
// - Builds are queued immediately, executed by Hangfire workers
// - Uses CancellationTokenRegistry for build cancellation
// - Retry creates a new build with the same parameters
// =============================================================================

using Ando.Server.BuildExecution;
using Ando.Server.Data;
using Ando.Server.Jobs;
using Ando.Server.Models;
using Hangfire;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Services;

/// <summary>
/// Implementation of build management operations.
/// </summary>
public class BuildService : IBuildService
{
    private readonly AndoDbContext _db;
    private readonly IBackgroundJobClient _jobClient;
    private readonly CancellationTokenRegistry _cancellationRegistry;
    private readonly ILogger<BuildService> _logger;

    public BuildService(
        AndoDbContext db,
        IBackgroundJobClient jobClient,
        CancellationTokenRegistry cancellationRegistry,
        ILogger<BuildService> logger)
    {
        _db = db;
        _jobClient = jobClient;
        _cancellationRegistry = cancellationRegistry;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> QueueBuildAsync(
        int projectId,
        string commitSha,
        string branch,
        BuildTrigger trigger,
        string? commitMessage = null,
        string? commitAuthor = null,
        int? pullRequestNumber = null)
    {
        // Create build record
        var build = new Build
        {
            ProjectId = projectId,
            CommitSha = commitSha,
            Branch = branch,
            Trigger = trigger,
            CommitMessage = commitMessage?.Length > 500 ? commitMessage[..500] : commitMessage,
            CommitAuthor = commitAuthor,
            PullRequestNumber = pullRequestNumber,
            Status = BuildStatus.Queued,
            QueuedAt = DateTime.UtcNow
        };

        _db.Builds.Add(build);
        await _db.SaveChangesAsync();

        // Update project last build time
        var project = await _db.Projects.FindAsync(projectId);
        if (project != null)
        {
            project.LastBuildAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        // Queue Hangfire job
        var jobId = _jobClient.Enqueue<ExecuteBuildJob>(
            job => job.ExecuteAsync(build.Id, CancellationToken.None));

        // Store job ID for potential cancellation
        build.HangfireJobId = jobId;
        await _db.SaveChangesAsync();

        _logger.LogInformation(
            "Queued build {BuildId} for project {ProjectId} (job: {JobId})",
            build.Id, projectId, jobId);

        return build.Id;
    }

    /// <inheritdoc />
    public async Task<Build?> GetBuildAsync(int buildId)
    {
        return await _db.Builds
            .Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == buildId);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Build>> GetBuildsForProjectAsync(int projectId, int skip = 0, int take = 20)
    {
        return await _db.Builds
            .Where(b => b.ProjectId == projectId)
            .OrderByDescending(b => b.QueuedAt)
            .Skip(skip)
            .Take(take)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Build>> GetRecentBuildsForUserAsync(int userId, int take = 10)
    {
        return await _db.Builds
            .Include(b => b.Project)
            .Where(b => b.Project.OwnerId == userId)
            .OrderByDescending(b => b.QueuedAt)
            .Take(take)
            .ToListAsync();
    }

    /// <inheritdoc />
    public async Task<bool> CancelBuildAsync(int buildId)
    {
        var build = await _db.Builds.FindAsync(buildId);
        if (build == null || !build.CanCancel)
        {
            return false;
        }

        // Try to cancel via registry (for running builds with active workers)
        if (_cancellationRegistry.TryCancel(buildId))
        {
            _logger.LogInformation("Cancelled running build {BuildId} via registry", buildId);
            return true;
        }

        // For queued builds, delete the Hangfire job
        if (build.Status == BuildStatus.Queued && !string.IsNullOrEmpty(build.HangfireJobId))
        {
            _jobClient.Delete(build.HangfireJobId);
        }

        // Mark build as cancelled (handles both queued and running builds without
        // an active cancellation token, such as test builds or orphaned builds)
        if (build.Status == BuildStatus.Queued || build.Status == BuildStatus.Running)
        {
            build.Status = BuildStatus.Cancelled;
            build.FinishedAt = DateTime.UtcNow;
            if (build.StartedAt.HasValue)
            {
                build.Duration = build.FinishedAt - build.StartedAt;
            }
            await _db.SaveChangesAsync();

            _logger.LogInformation("Cancelled build {BuildId} (status: {Status})", buildId, build.Status);
            return true;
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<int> RetryBuildAsync(int buildId)
    {
        var originalBuild = await _db.Builds
            .Include(b => b.Project)
            .FirstOrDefaultAsync(b => b.Id == buildId);

        if (originalBuild == null || !originalBuild.CanRetry)
        {
            throw new InvalidOperationException($"Build {buildId} cannot be retried");
        }

        // Queue a new build with the same parameters
        return await QueueBuildAsync(
            originalBuild.ProjectId,
            originalBuild.CommitSha,
            originalBuild.Branch,
            BuildTrigger.Manual,
            originalBuild.CommitMessage,
            originalBuild.CommitAuthor,
            originalBuild.PullRequestNumber);
    }

    /// <inheritdoc />
    public async Task UpdateBuildStatusAsync(
        int buildId,
        BuildStatus status,
        string? errorMessage = null,
        int? stepsTotal = null,
        int? stepsCompleted = null,
        int? stepsFailed = null)
    {
        var build = await _db.Builds.FindAsync(buildId);
        if (build == null)
        {
            return;
        }

        build.Status = status;

        if (status == BuildStatus.Running && build.StartedAt == null)
        {
            build.StartedAt = DateTime.UtcNow;
        }

        if (build.IsFinished && build.FinishedAt == null)
        {
            build.FinishedAt = DateTime.UtcNow;
            if (build.StartedAt.HasValue)
            {
                build.Duration = build.FinishedAt - build.StartedAt;
            }
        }

        if (errorMessage != null)
        {
            build.ErrorMessage = errorMessage.Length > 2000 ? errorMessage[..2000] : errorMessage;
        }

        if (stepsTotal.HasValue) build.StepsTotal = stepsTotal.Value;
        if (stepsCompleted.HasValue) build.StepsCompleted = stepsCompleted.Value;
        if (stepsFailed.HasValue) build.StepsFailed = stepsFailed.Value;

        await _db.SaveChangesAsync();
    }
}
