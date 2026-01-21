// =============================================================================
// UpdateProjectSettingsEndpoint.cs
//
// Summary: FastEndpoint for updating project settings.
//
// Updates project configuration including branch filter, PR builds,
// timeout, Docker image, profile, and notification settings.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Partial updates supported (all fields optional)
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// PUT /api/projects/{id}/settings - Update project settings.
/// </summary>
public class UpdateProjectSettingsEndpoint : Endpoint<UpdateProjectSettingsRequest, UpdateProjectSettingsResponse>
{
    private readonly IProjectService _projectService;

    public UpdateProjectSettingsEndpoint(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public override void Configure()
    {
        Put("/projects/{id}/settings");
    }

    public override async Task HandleAsync(UpdateProjectSettingsRequest req, CancellationToken ct)
    {
        var projectId = Route<int>("id");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var project = await _projectService.GetProjectForUserAsync(projectId, userId);
        if (project == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await _projectService.UpdateProjectSettingsAsync(
            projectId,
            req.BranchFilter,
            req.EnablePrBuilds,
            req.TimeoutMinutes,
            req.DockerImage,
            req.Profile,
            req.NotifyOnFailure,
            req.NotificationEmail);

        await SendAsync(new UpdateProjectSettingsResponse(true), cancellation: ct);
    }
}
