// =============================================================================
// GetBuildEndpoint.cs
//
// Summary: FastEndpoint for getting build details.
//
// Returns full build details including logs, artifacts, and status information.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership via build
// - Includes all logs and artifacts
// - Provides action flags (CanCancel, CanRetry, IsLive)
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Builds;
using Ando.Server.Data;
using Ando.Server.Models;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Builds;

/// <summary>
/// GET /api/builds/{id} - Get build details.
/// </summary>
public class GetBuildEndpoint : EndpointWithoutRequest<GetBuildResponse>
{
    private readonly AndoDbContext _db;

    public GetBuildEndpoint(AndoDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/builds/{id}");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var buildId = Route<int>("id");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var build = await _db.Builds
            .Include(b => b.Project)
            .Include(b => b.LogEntries.OrderBy(l => l.Sequence))
            .Include(b => b.Artifacts)
            .FirstOrDefaultAsync(b => b.Id == buildId, ct);

        if (build == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        // Verify ownership
        if (build.Project.OwnerId != userId)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var logEntries = build.LogEntries.Select(l => new LogEntryDto(
            l.Id,
            l.Sequence,
            l.Type.ToString(),
            l.Message,
            l.StepName,
            l.Timestamp
        )).ToList();

        var artifacts = build.Artifacts.Select(a => new ArtifactDto(
            a.Id,
            a.Name,
            FormatFileSize(a.SizeBytes),
            a.SizeBytes,
            a.CreatedAt
        )).ToList();

        await SendAsync(new GetBuildResponse(
            new BuildDetailsDto(
                build.Id,
                build.ProjectId,
                build.Project.RepoFullName,
                build.Project.RepoUrl,
                build.CommitSha,
                build.CommitSha.Length >= 8 ? build.CommitSha[..8] : build.CommitSha,
                build.Branch,
                build.CommitMessage,
                build.CommitAuthor,
                build.PullRequestNumber,
                build.Status.ToString(),
                build.Trigger.ToString(),
                build.QueuedAt,
                build.StartedAt,
                build.FinishedAt,
                build.Duration,
                build.StepsTotal,
                build.StepsCompleted,
                build.StepsFailed,
                build.ErrorMessage,
                build.Status == BuildStatus.Queued || build.Status == BuildStatus.Running,
                build.Status == BuildStatus.Failed || build.Status == BuildStatus.Cancelled || build.Status == BuildStatus.TimedOut,
                build.Status == BuildStatus.Queued || build.Status == BuildStatus.Running,
                logEntries,
                artifacts
            )
        ), cancellation: ct);
    }

    private static string FormatFileSize(long bytes)
    {
        string[] sizes = ["B", "KB", "MB", "GB", "TB"];
        int order = 0;
        double size = bytes;

        while (size >= 1024 && order < sizes.Length - 1)
        {
            order++;
            size /= 1024;
        }

        return $"{size:0.##} {sizes[order]}";
    }
}
