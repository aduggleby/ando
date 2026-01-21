// =============================================================================
// SetSecretEndpoint.cs
//
// Summary: FastEndpoint for adding or updating a project secret.
//
// Sets an environment variable secret for use during builds. Secrets are
// encrypted at rest and never returned in API responses.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Validates secret name format (uppercase with underscores)
// - Upserts (creates or updates) the secret
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// POST /api/projects/{id}/secrets - Add or update a secret.
/// </summary>
public class SetSecretEndpoint : Endpoint<SetSecretRequest, SecretResponse>
{
    private readonly IProjectService _projectService;

    public SetSecretEndpoint(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public override void Configure()
    {
        Post("/projects/{id}/secrets");
    }

    public override async Task HandleAsync(SetSecretRequest req, CancellationToken ct)
    {
        var projectId = Route<int>("id");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var project = await _projectService.GetProjectForUserAsync(projectId, userId);
        if (project == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Value))
        {
            await SendAsync(new SecretResponse(false, "Secret name and value are required."), cancellation: ct);
            return;
        }

        await _projectService.SetSecretAsync(projectId, req.Name.Trim(), req.Value);

        await SendAsync(new SecretResponse(true), cancellation: ct);
    }
}
