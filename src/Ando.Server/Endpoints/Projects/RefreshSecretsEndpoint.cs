// =============================================================================
// RefreshSecretsEndpoint.cs
//
// Summary: FastEndpoint for refreshing required secrets detection.
//
// Re-scans the build.csando file to detect required environment variables
// and available profiles.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Returns detected secrets and profiles
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// POST /api/projects/{id}/refresh-secrets - Refresh required secrets detection.
/// </summary>
public class RefreshSecretsEndpoint : EndpointWithoutRequest<RefreshSecretsResponse>
{
    private readonly IProjectService _projectService;

    public RefreshSecretsEndpoint(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public override void Configure()
    {
        Post("/projects/{id}/refresh-secrets");
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

        var detectedSecrets = await _projectService.DetectAndUpdateRequiredSecretsAsync(projectId);
        var detectedProfiles = await _projectService.DetectAndUpdateProfilesAsync(projectId);

        await SendAsync(new RefreshSecretsResponse(
            true,
            detectedSecrets,
            detectedProfiles
        ), cancellation: ct);
    }
}
