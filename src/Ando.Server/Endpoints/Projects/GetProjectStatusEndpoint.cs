// =============================================================================
// GetProjectStatusEndpoint.cs
//
// Summary: FastEndpoint for getting project deployment status.
//
// Returns all projects with their deployment status (deployed, failed,
// not deployed) for the status dashboard view.
//
// Design Decisions:
// - Requires authentication
// - Supports sorting by different fields
// - Deployment status derived from last build
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// GET /api/projects/status - Get project status dashboard.
/// </summary>
public class GetProjectStatusEndpoint : EndpointWithoutRequest<GetProjectsStatusResponse>
{
    private readonly AndoDbContext _db;
    private readonly IProjectService _projectService;

    public GetProjectStatusEndpoint(AndoDbContext db, IProjectService projectService)
    {
        _db = db;
        _projectService = projectService;
    }

    public override void Configure()
    {
        Get("/projects/status");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var sortBy = Query<string>("sortBy", isRequired: false) ?? "Alphabetical";
        var direction = Query<string>("direction", isRequired: false) ?? "Ascending";

        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var projects = await _projectService.GetProjectsForUserAsync(userId);

        var statusItems = new List<ProjectStatusDto>();
        foreach (var project in projects)
        {
            var lastBuild = await _db.Builds
                .Where(b => b.ProjectId == project.Id)
                .OrderByDescending(b => b.QueuedAt)
                .FirstOrDefaultAsync(ct);

            var buildCount = await _db.Builds
                .Where(b => b.ProjectId == project.Id)
                .CountAsync(ct);

            var deploymentStatus = "NotDeployed";
            if (lastBuild != null)
            {
                deploymentStatus = lastBuild.Status switch
                {
                    BuildStatus.Success => "Deployed",
                    BuildStatus.Failed or BuildStatus.TimedOut => "Failed",
                    _ => "NotDeployed"
                };
            }

            statusItems.Add(new ProjectStatusDto(
                project.Id,
                project.RepoFullName,
                project.RepoUrl,
                project.CreatedAt,
                lastBuild?.FinishedAt ?? lastBuild?.QueuedAt,
                deploymentStatus,
                buildCount
            ));
        }

        // Apply sorting
        IEnumerable<ProjectStatusDto> sorted = sortBy switch
        {
            "Alphabetical" => direction == "Ascending"
                ? statusItems.OrderBy(p => p.RepoFullName)
                : statusItems.OrderByDescending(p => p.RepoFullName),
            "LastDeployment" => direction == "Ascending"
                ? statusItems.OrderBy(p => p.LastDeploymentAt ?? DateTime.MinValue)
                : statusItems.OrderByDescending(p => p.LastDeploymentAt ?? DateTime.MinValue),
            "CreatedDate" => direction == "Ascending"
                ? statusItems.OrderBy(p => p.CreatedAt)
                : statusItems.OrderByDescending(p => p.CreatedAt),
            _ => statusItems.OrderBy(p => p.RepoFullName)
        };

        await SendAsync(new GetProjectsStatusResponse(
            sorted.ToList(),
            sortBy,
            direction
        ), cancellation: ct);
    }
}
