// =============================================================================
// CleanupArtifactsJob.cs
//
// Summary: Hangfire job for cleaning up expired build artifacts.
//
// Runs on a schedule to delete artifacts that have passed their expiration date.
// Deletes both the database records and the physical files from storage.
//
// Design Decisions:
// - Processes in batches to avoid long-running transactions
// - Logs warnings for missing files but continues processing
// - Uses configurable batch size for memory efficiency
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Configuration;
using Ando.Server.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Jobs;

/// <summary>
/// Hangfire job that cleans up expired build artifacts.
/// </summary>
public class CleanupArtifactsJob
{
    private readonly AndoDbContext _db;
    private readonly ILogger<CleanupArtifactsJob> _logger;
    private readonly StorageSettings _storageSettings;
    private const int BatchSize = 100;

    public CleanupArtifactsJob(
        AndoDbContext db,
        ILogger<CleanupArtifactsJob> logger,
        IOptions<StorageSettings> storageSettings)
    {
        _db = db;
        _logger = logger;
        _storageSettings = storageSettings.Value;
    }

    /// <summary>
    /// Executes the artifact cleanup job.
    /// </summary>
    public async Task ExecuteAsync()
    {
        _logger.LogInformation("Starting artifact cleanup job");

        var totalDeleted = 0;
        var totalFilesDeleted = 0;
        var totalBytesFreed = 0L;

        while (true)
        {
            // Get a batch of expired artifacts
            var expiredArtifacts = await _db.BuildArtifacts
                .Where(a => a.ExpiresAt <= DateTime.UtcNow)
                .OrderBy(a => a.ExpiresAt)
                .Take(BatchSize)
                .ToListAsync();

            if (expiredArtifacts.Count == 0)
            {
                break;
            }

            foreach (var artifact in expiredArtifacts)
            {
                var artifactPath = ArtifactPathResolver.ResolveAbsolutePath(
                    _storageSettings.ArtifactsPath,
                    artifact.StoragePath);

                // Delete the physical file
                if (!string.IsNullOrWhiteSpace(artifactPath) &&
                    ArtifactPathResolver.IsWithinRoot(_storageSettings.ArtifactsPath, artifactPath) &&
                    File.Exists(artifactPath))
                {
                    try
                    {
                        File.Delete(artifactPath);
                        totalFilesDeleted++;
                        totalBytesFreed += artifact.SizeBytes;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Failed to delete artifact file {Path} for artifact {ArtifactId}",
                            artifactPath,
                            artifact.Id);
                    }
                }
                else
                {
                    _logger.LogWarning(
                        "Artifact file not found: {Path} for artifact {ArtifactId}",
                        artifactPath,
                        artifact.Id);
                }

                // Remove the database record
                _db.BuildArtifacts.Remove(artifact);
            }

            await _db.SaveChangesAsync();
            totalDeleted += expiredArtifacts.Count;

            _logger.LogDebug(
                "Deleted batch of {Count} expired artifacts",
                expiredArtifacts.Count);
        }

        if (totalDeleted > 0)
        {
            _logger.LogInformation(
                "Artifact cleanup complete: deleted {Count} artifacts, {Files} files, freed {Bytes:N0} bytes",
                totalDeleted,
                totalFilesDeleted,
                totalBytesFreed);
        }
        else
        {
            _logger.LogInformation("Artifact cleanup complete: no expired artifacts found");
        }
    }
}
