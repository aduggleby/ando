// =============================================================================
// DeleteProjectEndpoint.cs
//
// Summary: FastEndpoint for deleting a project.
//
// Deletes a project and all associated builds, logs, and artifacts.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Cascades deletion to all related data
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// DELETE /api/projects/{id} - Delete a project.
/// </summary>
public class DeleteProjectEndpoint : EndpointWithoutRequest<DeleteProjectResponse>
{
    private readonly IProjectService _projectService;

    public DeleteProjectEndpoint(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public override void Configure()
    {
        Delete("/projects/{id}");
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

        await _projectService.DeleteProjectAsync(projectId);

        await SendAsync(new DeleteProjectResponse(true), cancellation: ct);
    }
}
