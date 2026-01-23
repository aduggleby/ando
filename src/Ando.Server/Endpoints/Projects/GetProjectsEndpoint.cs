// =============================================================================
// GetProjectsEndpoint.cs
//
// Summary: FastEndpoint for listing user's projects.
//
// Returns all projects owned by the authenticated user with their build
// status and configuration state.
//
// Design Decisions:
// - Requires authentication
// - Only returns user's own projects
// - Includes last build status and missing secrets count
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Data;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// GET /api/projects - List user's projects.
/// </summary>
public class GetProjectsEndpoint : EndpointWithoutRequest<GetProjectsResponse>
{
    private readonly AndoDbContext _db;
    private readonly IProjectService _projectService;

    public GetProjectsEndpoint(AndoDbContext db, IProjectService projectService)
    {
        _db = db;
        _projectService = projectService;
    }

    public override void Configure()
    {
        Get("/projects");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var projects = await _projectService.GetProjectsForUserAsync(userId);

        var projectItems = new List<ProjectListItemDto>();
        foreach (var project in projects)
        {
            var lastBuild = await _db.Builds
                .Where(b => b.ProjectId == project.Id)
                .OrderByDescending(b => b.QueuedAt)
                .FirstOrDefaultAsync(ct);

            var buildCount = await _db.Builds
                .Where(b => b.ProjectId == project.Id)
                .CountAsync(ct);

            var secrets = await _db.ProjectSecrets
                .Where(s => s.ProjectId == project.Id)
                .Select(s => s.Name)
                .ToListAsync(ct);
            var missingSecrets = project.GetMissingSecretsFrom(secrets);

            projectItems.Add(new ProjectListItemDto(
                project.Id,
                project.RepoFullName,
                project.RepoUrl,
                project.CreatedAt,
                project.LastBuildAt,
                lastBuild?.Status.ToString(),
                buildCount,
                missingSecrets.Count == 0,
                missingSecrets.Count
            ));
        }

        await SendAsync(new GetProjectsResponse(projectItems), cancellation: ct);
    }
}
