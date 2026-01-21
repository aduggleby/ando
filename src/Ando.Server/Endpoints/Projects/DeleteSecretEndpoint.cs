// =============================================================================
// DeleteSecretEndpoint.cs
//
// Summary: FastEndpoint for deleting a project secret.
//
// Removes an environment variable secret from the project.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Idempotent (no error if secret doesn't exist)
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// DELETE /api/projects/{id}/secrets/{name} - Delete a secret.
/// </summary>
public class DeleteSecretEndpoint : EndpointWithoutRequest<SecretResponse>
{
    private readonly IProjectService _projectService;

    public DeleteSecretEndpoint(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public override void Configure()
    {
        Delete("/projects/{id}/secrets/{name}");
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var projectId = Route<int>("id");
        var secretName = Route<string>("name");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var project = await _projectService.GetProjectForUserAsync(projectId, userId);
        if (project == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        await _projectService.DeleteSecretAsync(projectId, secretName ?? "");

        await SendAsync(new SecretResponse(true), cancellation: ct);
    }
}
