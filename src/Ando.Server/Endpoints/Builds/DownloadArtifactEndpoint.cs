// =============================================================================
// DownloadArtifactEndpoint.cs
//
// Summary: FastEndpoint for downloading build artifacts.
//
// Serves artifact files from storage with appropriate content type.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership via build
// - Returns 404 if file doesn't exist on disk
// - Uses generic content type for all files
// =============================================================================

using System.Security.Claims;
using Ando.Server.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Builds;

/// <summary>
/// GET /api/builds/{buildId}/artifacts/{artifactId} - Download artifact.
/// </summary>
public class DownloadArtifactEndpoint : EndpointWithoutRequest
{
    private readonly AndoDbContext _db;
    private readonly ILogger<DownloadArtifactEndpoint> _logger;

    public DownloadArtifactEndpoint(AndoDbContext db, ILogger<DownloadArtifactEndpoint> logger)
    {
        _db = db;
        _logger = logger;
    }

    public override void Configure()
    {
        Get("/builds/{buildId}/artifacts/{artifactId}");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var buildId = Route<int>("buildId");
        var artifactId = Route<int>("artifactId");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var artifact = await _db.BuildArtifacts
            .Include(a => a.Build)
            .ThenInclude(b => b.Project)
            .FirstOrDefaultAsync(a => a.Id == artifactId && a.BuildId == buildId, ct);

        if (artifact == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        // Verify ownership
        if (artifact.Build.Project.OwnerId != userId)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (!System.IO.File.Exists(artifact.StoragePath))
        {
            _logger.LogWarning("Artifact file not found: {Path}", artifact.StoragePath);
            await SendNotFoundAsync(ct);
            return;
        }

        await SendFileAsync(
            new FileInfo(artifact.StoragePath),
            artifact.Name,
            cancellation: ct);
    }
}
