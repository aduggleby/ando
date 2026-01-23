// =============================================================================
// TriggerBuildEndpoint.cs
//
// Summary: FastEndpoint for manually triggering a build.
//
// Queues a new build for the specified project. Automatically detects
// required secrets and validates project configuration before building.
//
// Design Decisions:
// - Requires authentication
// - Verifies project ownership
// - Validates all required secrets are configured
// - Gets latest commit from GitHub if available
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Projects;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Projects;

/// <summary>
/// POST /api/projects/{id}/build - Trigger a manual build.
/// </summary>
public class TriggerBuildEndpoint : Endpoint<TriggerBuildRequest, TriggerBuildResponse>
{
    private readonly AndoDbContext _db;
    private readonly IProjectService _projectService;
    private readonly IBuildService _buildService;
    private readonly IGitHubService _gitHubService;

    public TriggerBuildEndpoint(
        AndoDbContext db,
        IProjectService projectService,
        IBuildService buildService,
        IGitHubService gitHubService)
    {
        _db = db;
        _projectService = projectService;
        _buildService = buildService;
        _gitHubService = gitHubService;
    }

    public override void Configure()
    {
        Post("/projects/{id}/build");
    }

    public override async Task HandleAsync(TriggerBuildRequest req, CancellationToken ct)
    {
        var projectId = Route<int>("id");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");

        var project = await _projectService.GetProjectForUserAsync(projectId, userId);
        if (project == null)
        {
            await SendNotFoundAsync(ct);
            return;
        }

        // Re-detect required secrets from the build script (catches new env vars)
        await _projectService.DetectAndUpdateRequiredSecretsAsync(projectId);

        // Reload to get updated RequiredSecrets
        await _db.Entry(project).ReloadAsync(ct);

        // Check if project is properly configured
        var secretNames = await _projectService.GetSecretNamesAsync(projectId);
        var missingSecrets = project.GetMissingSecretsFrom(secretNames);

        if (missingSecrets.Count > 0)
        {
            await SendAsync(new TriggerBuildResponse(
                false,
                Error: $"Cannot start build: missing required secrets: {string.Join(", ", missingSecrets)}. Configure them in project settings."
            ), cancellation: ct);
            return;
        }

        var targetBranch = req.Branch ?? project.DefaultBranch;

        // Get the latest commit SHA for the branch from GitHub
        string commitSha = "HEAD";
        string commitMessage = "Manual build trigger";

        if (project.InstallationId.HasValue)
        {
            var sha = await _gitHubService.GetBranchHeadShaAsync(
                project.InstallationId.Value,
                project.RepoFullName,
                targetBranch);

            if (!string.IsNullOrEmpty(sha))
            {
                commitSha = sha;
                commitMessage = $"Manual build of {targetBranch}";
            }
        }

        // Queue a manual build
        var buildId = await _buildService.QueueBuildAsync(
            projectId,
            commitSha,
            targetBranch,
            BuildTrigger.Manual,
            commitMessage,
            User.Identity?.Name);

        await SendAsync(new TriggerBuildResponse(true, buildId), cancellation: ct);
    }
}
