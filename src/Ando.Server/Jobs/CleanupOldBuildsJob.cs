// =============================================================================
// CleanupOldBuildsJob.cs
//
// Summary: Hangfire job for cleaning up stale build data.
//
// Handles cleanup of builds stuck in intermediate states (Running/Queued) that
// may have been orphaned due to server restarts or crashes.
//
// Design Decisions:
// - Marks builds as TimedOut if they've been running longer than max timeout
// - Marks queued builds as Failed if they've been waiting too long
// - Does not delete build history (requirements specify full history retention)
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Jobs;

/// <summary>
/// Hangfire job that cleans up stale/orphaned builds.
/// </summary>
public class CleanupOldBuildsJob
{
    private readonly AndoDbContext _db;
    private readonly ILogger<CleanupOldBuildsJob> _logger;

    // Builds stuck in Running state for more than this duration are considered orphaned
    private static readonly TimeSpan MaxRunningDuration = TimeSpan.FromHours(2);

    // Builds stuck in Queued state for more than this duration are considered orphaned
    private static readonly TimeSpan MaxQueuedDuration = TimeSpan.FromHours(24);

    public CleanupOldBuildsJob(
        AndoDbContext db,
        ILogger<CleanupOldBuildsJob> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Executes the stale build cleanup job.
    /// </summary>
    public async Task ExecuteAsync()
    {
        _logger.LogInformation("Starting stale build cleanup job");

        var orphanedRunning = await CleanupOrphanedRunningBuildsAsync();
        var orphanedQueued = await CleanupOrphanedQueuedBuildsAsync();

        if (orphanedRunning > 0 || orphanedQueued > 0)
        {
            _logger.LogInformation(
                "Stale build cleanup complete: {Running} orphaned running builds, {Queued} orphaned queued builds",
                orphanedRunning,
                orphanedQueued);
        }
        else
        {
            _logger.LogInformation("Stale build cleanup complete: no orphaned builds found");
        }
    }

    /// <summary>
    /// Marks builds that have been running too long as timed out.
    /// </summary>
    private async Task<int> CleanupOrphanedRunningBuildsAsync()
    {
        var cutoff = DateTime.UtcNow - MaxRunningDuration;

        var orphanedBuilds = await _db.Builds
            .Where(b => b.Status == BuildStatus.Running)
            .Where(b => b.StartedAt < cutoff)
            .ToListAsync();

        foreach (var build in orphanedBuilds)
        {
            _logger.LogWarning(
                "Marking orphaned running build {BuildId} as timed out (started at {StartedAt})",
                build.Id,
                build.StartedAt);

            build.Status = BuildStatus.TimedOut;
            build.FinishedAt = DateTime.UtcNow;
            build.ErrorMessage = "Build was terminated: exceeded maximum running time or server was restarted.";

            if (build.StartedAt.HasValue)
            {
                build.Duration = build.FinishedAt.Value - build.StartedAt.Value;
            }
        }

        if (orphanedBuilds.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return orphanedBuilds.Count;
    }

    /// <summary>
    /// Marks builds that have been queued too long as failed.
    /// </summary>
    private async Task<int> CleanupOrphanedQueuedBuildsAsync()
    {
        var cutoff = DateTime.UtcNow - MaxQueuedDuration;

        var orphanedBuilds = await _db.Builds
            .Where(b => b.Status == BuildStatus.Queued)
            .Where(b => b.QueuedAt < cutoff)
            .ToListAsync();

        foreach (var build in orphanedBuilds)
        {
            _logger.LogWarning(
                "Marking orphaned queued build {BuildId} as failed (queued at {QueuedAt})",
                build.Id,
                build.QueuedAt);

            build.Status = BuildStatus.Failed;
            build.FinishedAt = DateTime.UtcNow;
            build.ErrorMessage = "Build was never started: exceeded maximum queue time.";
        }

        if (orphanedBuilds.Count > 0)
        {
            await _db.SaveChangesAsync();
        }

        return orphanedBuilds.Count;
    }
}
