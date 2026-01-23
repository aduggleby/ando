// =============================================================================
// WebhooksController.cs
//
// Summary: Handles incoming GitHub webhook events.
//
// Receives push and pull request events from GitHub, validates signatures,
// and queues builds for matching projects. This is the entry point for
// all automated build triggers.
//
// Design Decisions:
// - Validates signature before processing any payload
// - Returns 200 OK even for ignored events (GitHub retries on non-2xx)
// - Queues builds asynchronously via Hangfire
// =============================================================================

using System.Text.Json;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Models;
using Ando.Server.Services;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Controllers;

/// <summary>
/// Controller for handling GitHub webhook events.
/// </summary>
[ApiController]
[Route("webhooks")]
[AllowAnonymous]
public class WebhooksController : ControllerBase
{
    private readonly AndoDbContext _db;
    private readonly GitHubSettings _gitHubSettings;
    private readonly IBuildService _buildService;
    private readonly IProjectService _projectService;
    private readonly ILogger<WebhooksController> _logger;

    public WebhooksController(
        AndoDbContext db,
        IOptions<GitHubSettings> gitHubSettings,
        IBuildService buildService,
        IProjectService projectService,
        ILogger<WebhooksController> logger)
    {
        _db = db;
        _gitHubSettings = gitHubSettings.Value;
        _buildService = buildService;
        _projectService = projectService;
        _logger = logger;
    }

    /// <summary>
    /// GitHub webhook endpoint.
    /// </summary>
    [HttpPost("github")]
    public async Task<IActionResult> GitHub()
    {
        // Read raw body for signature verification
        Request.EnableBuffering();
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync();
        Request.Body.Position = 0;

        var bodyBytes = System.Text.Encoding.UTF8.GetBytes(body);

        // Validate signature
        var signature = Request.Headers["X-Hub-Signature-256"].FirstOrDefault()
            ?? Request.Headers["X-Hub-Signature"].FirstOrDefault();

        var validator = new WebhookSignatureValidator(_gitHubSettings.WebhookSecret);
        if (!validator.Validate(signature, bodyBytes))
        {
            _logger.LogWarning("Invalid webhook signature");
            return Unauthorized(new { error = "Invalid signature" });
        }

        // Get event type
        var eventType = Request.Headers["X-GitHub-Event"].FirstOrDefault();
        var deliveryId = Request.Headers["X-GitHub-Delivery"].FirstOrDefault();

        _logger.LogInformation("Received webhook: {Event} (delivery: {DeliveryId})", eventType, deliveryId);

        // Route to appropriate handler
        return eventType switch
        {
            "push" => await HandlePushEventAsync(body),
            "pull_request" => await HandlePullRequestEventAsync(body),
            "ping" => Ok(new { message = "pong" }),
            _ => Ok(new { message = $"Event '{eventType}' ignored" })
        };
    }

    /// <summary>
    /// Handles push events.
    /// </summary>
    private async Task<IActionResult> HandlePushEventAsync(string body)
    {
        var payload = JsonSerializer.Deserialize<PushEventPayload>(body);
        if (payload?.Repository == null)
        {
            _logger.LogWarning("Invalid push event payload");
            return BadRequest(new { error = "Invalid payload" });
        }

        // Skip if this is a branch deletion (after SHA is all zeros)
        if (payload.After == "0000000000000000000000000000000000000000")
        {
            _logger.LogInformation("Ignoring branch deletion event");
            return Ok(new { message = "Branch deletion ignored" });
        }

        // Find ALL matching projects (multiple users can add the same repo)
        var projects = await _db.Projects
            .Where(p => p.GitHubRepoId == payload.Repository.Id)
            .ToListAsync();

        if (projects.Count == 0)
        {
            _logger.LogInformation(
                "No project configured for repository {Repo}",
                payload.Repository.FullName);
            return Ok(new { message = "Repository not configured" });
        }

        var buildIds = new List<int>();

        foreach (var project in projects)
        {
            // Check branch filter for this project
            if (!project.MatchesBranchFilter(payload.Branch))
            {
                _logger.LogInformation(
                    "Branch {Branch} does not match filter for project {ProjectId}",
                    payload.Branch, project.Id);
                continue;
            }

            // Re-detect required secrets from the build script (catches new env vars)
            await _projectService.DetectAndUpdateRequiredSecretsAsync(project.Id);

            // Reload project to get updated RequiredSecrets
            await _db.Entry(project).ReloadAsync();

            // Check if project is properly configured (all required secrets are set)
            var secretNames = await _projectService.GetSecretNamesAsync(project.Id);
            var missingSecrets = project.GetMissingSecretsFrom(secretNames);
            if (missingSecrets.Count > 0)
            {
                _logger.LogWarning(
                    "Project {ProjectId} is missing required secrets: {MissingSecrets}. Skipping build.",
                    project.Id, string.Join(", ", missingSecrets));
                continue;
            }

            // Update installation ID if changed
            if (payload.Installation != null && project.InstallationId != payload.Installation.Id)
            {
                project.InstallationId = payload.Installation.Id;
            }

            // Queue build for this project
            var buildId = await _buildService.QueueBuildAsync(
                project.Id,
                payload.CommitSha,
                payload.Branch,
                BuildTrigger.Push,
                payload.HeadCommit?.Message,
                payload.HeadCommit?.Author?.Name);

            buildIds.Add(buildId);

            _logger.LogInformation(
                "Queued build {BuildId} for {Repo} ({Branch}, {Sha})",
                buildId, project.RepoFullName, payload.Branch, payload.CommitSha[..8]);
        }

        // Save any installation ID updates
        await _db.SaveChangesAsync();

        if (buildIds.Count == 0)
        {
            return Ok(new { message = "No builds queued (branch filter)" });
        }

        return Ok(new { message = $"{buildIds.Count} build(s) queued", buildIds });
    }

    /// <summary>
    /// Handles pull request events.
    /// </summary>
    private async Task<IActionResult> HandlePullRequestEventAsync(string body)
    {
        var payload = JsonSerializer.Deserialize<PullRequestEventPayload>(body);
        if (payload?.PullRequest?.Head == null || payload.Repository == null)
        {
            _logger.LogWarning("Invalid pull_request event payload");
            return BadRequest(new { error = "Invalid payload" });
        }

        // Only build on opened, synchronize (new commits), or reopened
        var buildableActions = new[] { "opened", "synchronize", "reopened" };
        if (!buildableActions.Contains(payload.Action))
        {
            _logger.LogInformation("Ignoring PR action: {Action}", payload.Action);
            return Ok(new { message = $"Action '{payload.Action}' ignored" });
        }

        // Find ALL matching projects (multiple users can add the same repo)
        var projects = await _db.Projects
            .Where(p => p.GitHubRepoId == payload.Repository.Id)
            .ToListAsync();

        if (projects.Count == 0)
        {
            _logger.LogInformation(
                "No project configured for repository {Repo}",
                payload.Repository.FullName);
            return Ok(new { message = "Repository not configured" });
        }

        var buildIds = new List<int>();

        foreach (var project in projects)
        {
            // Check if PR builds are enabled for this project
            if (!project.EnablePrBuilds)
            {
                _logger.LogInformation(
                    "PR builds not enabled for project {ProjectId}",
                    project.Id);
                continue;
            }

            // Re-detect required secrets from the build script (catches new env vars)
            await _projectService.DetectAndUpdateRequiredSecretsAsync(project.Id);

            // Reload project to get updated RequiredSecrets
            await _db.Entry(project).ReloadAsync();

            // Check if project is properly configured (all required secrets are set)
            var secretNames = await _projectService.GetSecretNamesAsync(project.Id);
            var missingSecrets = project.GetMissingSecretsFrom(secretNames);
            if (missingSecrets.Count > 0)
            {
                _logger.LogWarning(
                    "Project {ProjectId} is missing required secrets: {MissingSecrets}. Skipping build.",
                    project.Id, string.Join(", ", missingSecrets));
                continue;
            }

            // Update installation ID if changed
            if (payload.Installation != null && project.InstallationId != payload.Installation.Id)
            {
                project.InstallationId = payload.Installation.Id;
            }

            // Queue build for this project
            var buildId = await _buildService.QueueBuildAsync(
                project.Id,
                payload.PullRequest.Head.Sha,
                payload.PullRequest.Head.Ref,
                BuildTrigger.PullRequest,
                $"PR #{payload.Number}: {payload.PullRequest.Title}",
                payload.PullRequest.User?.Login,
                payload.Number);

            buildIds.Add(buildId);

            _logger.LogInformation(
                "Queued PR build {BuildId} for {Repo} (PR #{Number}, {Sha})",
                buildId, project.RepoFullName, payload.Number, payload.PullRequest.Head.Sha[..8]);
        }

        // Save any installation ID updates
        await _db.SaveChangesAsync();

        if (buildIds.Count == 0)
        {
            return Ok(new { message = "No builds queued (PR builds disabled)" });
        }

        return Ok(new { message = $"{buildIds.Count} build(s) queued", buildIds });
    }
}
