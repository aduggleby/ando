// =============================================================================
// GetProjectEndpoint.cs
//
// Summary: FastEndpoint for getting project details.
//
// Returns full project details including recent builds for the specified
// project owned by the authenticated user.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Includes recent builds and configuration status
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Data;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// GET /api/projects/{id} - Get project details.
/// </summary>
public class GetProjectEndpoint : EndpointWithoutRequest<GetProjectResponse>
{
    private readonly AndoDbContext _db;
    private readonly IProjectService _projectService;
    private readonly IBuildService _buildService;

    public GetProjectEndpoint(
        AndoDbContext db,
        IProjectService projectService,
        IBuildService buildService)
    {
        _db = db;
        _projectService = projectService;
        _buildService = buildService;
    }

    public override void Configure()
    {
        Get("/projects/{id}");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var projectId = Route<int>("id");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var project = await _projectService.GetProjectForUserAsync(projectId, userId);
        if (project == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        var recentBuilds = await _buildService.GetBuildsForProjectAsync(projectId, 0, 20);
        var totalBuilds = await _db.Builds.CountAsync(b => b.ProjectId == projectId, ct);
        var secretNames = await _projectService.GetSecretNamesAsync(projectId);
        var missingSecrets = project.GetMissingSecretsFrom(secretNames);

        var buildItems = recentBuilds.Select(b => new BuildListItemDto(
            b.Id,
            b.CommitSha,
            b.CommitSha.Length >= 8 ? b.CommitSha[..8] : b.CommitSha,
            b.Branch,
            b.CommitMessage,
            b.CommitAuthor,
            b.Status.ToString(),
            b.Trigger.ToString(),
            b.QueuedAt,
            b.StartedAt,
            b.FinishedAt,
            b.Duration,
            b.PullRequestNumber
        )).ToList();

        await SendAsync(new GetProjectResponse(
            new ProjectDetailsDto(
                project.Id,
                project.RepoFullName,
                project.RepoUrl,
                project.DefaultBranch,
                project.BranchFilter,
                project.Profile,
                project.EnablePrBuilds,
                project.TimeoutMinutes,
                project.CreatedAt,
                project.LastBuildAt,
                totalBuilds,
                missingSecrets.Count == 0,
                missingSecrets,
                buildItems
            )
        ), cancellation: ct);
    }
}
