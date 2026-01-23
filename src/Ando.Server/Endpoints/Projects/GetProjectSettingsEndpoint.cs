// =============================================================================
// GetProjectSettingsEndpoint.cs
//
// Summary: FastEndpoint for getting project settings.
//
// Returns project configuration including build settings, secrets (names only),
// and notification preferences.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Never returns secret values, only names
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// GET /api/projects/{id}/settings - Get project settings.
/// </summary>
public class GetProjectSettingsEndpoint : EndpointWithoutRequest<GetProjectSettingsResponse>
{
    private readonly IProjectService _projectService;

    public GetProjectSettingsEndpoint(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public override void Configure()
    {
        Get("/projects/{id}/settings");
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

        var secretNames = await _projectService.GetSecretNamesAsync(projectId);
        var missingSecrets = project.GetMissingSecretsFrom(secretNames);

        await SendAsync(new GetProjectSettingsResponse(
            new ProjectSettingsDto(
                project.Id,
                project.RepoFullName,
                project.BranchFilter,
                project.EnablePrBuilds,
                project.TimeoutMinutes,
                project.DockerImage,
                project.Profile,
                project.GetAvailableProfileNames(),
                project.IsProfileValid(),
                project.RequiredSecrets,
                project.NotifyOnFailure,
                project.NotificationEmail,
                secretNames,
                missingSecrets
            )
        ), cancellation: ct);
    }
}
