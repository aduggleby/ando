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
// - Validates the secret name with ProjectSecret.IsValidName (environment
//   variable name, ASCII letters/digits/underscore, max 100 characters) before
//   calling the service; FastEndpoints does not enforce the DTO's
//   DataAnnotations, so the check is explicit here
// - Upserts (creates or updates) the secret
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Models;
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
            await Send.NotFoundAsync(ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Value))
        {
            await Send.OkAsync(new SecretResponse(false, "Secret name and value are required."), cancellation: ct);
            return;
        }

        var name = req.Name.Trim();
        if (!ProjectSecret.IsValidName(name))
        {
            await Send.OkAsync(new SecretResponse(false, ProjectSecret.NameRuleDescription), cancellation: ct);
            return;
        }

        await _projectService.SetSecretAsync(projectId, name, req.Value);

        await Send.OkAsync(new SecretResponse(true), cancellation: ct);
    }
}
