// =============================================================================
// BulkImportSecretsEndpoint.cs
//
// Summary: FastEndpoint for bulk importing secrets from .env format.
//
// Parses KEY=value lines from the input, ignoring comments and empty lines.
// Normalizes secret names to uppercase and validates them with
// ProjectSecret.IsValidName (format and 100-character limit).
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Continues on individual errors (reports all at end)
// - Handles quoted values
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// POST /api/projects/{id}/secrets/bulk - Bulk import secrets.
/// </summary>
public class BulkImportSecretsEndpoint : Endpoint<BulkImportSecretsRequest, BulkImportSecretsResponse>
{
    private readonly IProjectService _projectService;

    public BulkImportSecretsEndpoint(IProjectService projectService)
    {
        _projectService = projectService;
    }

    public override void Configure()
    {
        Post("/projects/{id}/secrets/bulk");
    }

    public override async Task HandleAsync(BulkImportSecretsRequest req, CancellationToken ct)
    {
        var projectId = Route<int>("id");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var project = await _projectService.GetProjectForUserAsync(projectId, userId);
        if (project == null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        if (string.IsNullOrWhiteSpace(req.Content))
        {
            await Send.OkAsync(new BulkImportSecretsResponse(false, 0, ["No content provided."]), cancellation: ct);
            return;
        }

        var lines = req.Content.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
        var imported = 0;
        var errors = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Parse KEY=value format
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
            {
                errors.Add($"Invalid format: {trimmed[..Math.Min(20, trimmed.Length)]}...");
                continue;
            }

            var name = trimmed[..equalsIndex].Trim();
            var value = trimmed[(equalsIndex + 1)..].Trim();

            // Remove surrounding quotes from value if present
            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            // Normalize name to uppercase
            name = name.ToUpperInvariant().Replace('-', '_');

            // Validate secret name
            if (!ProjectSecret.IsValidName(name))
            {
                errors.Add($"Invalid name: {name[..Math.Min(40, name.Length)]}");
                continue;
            }

            if (string.IsNullOrEmpty(value))
            {
                errors.Add($"Empty value for: {name}");
                continue;
            }

            await _projectService.SetSecretAsync(projectId, name, value);
            imported++;
        }

        await Send.OkAsync(new BulkImportSecretsResponse(
            errors.Count == 0,
            imported,
            errors.Count > 0 ? errors : null
        ), cancellation: ct);
    }
}
