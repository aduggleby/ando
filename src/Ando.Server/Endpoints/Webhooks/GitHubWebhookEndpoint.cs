// =============================================================================
// GitHubWebhookEndpoint.cs
//
// Summary: FastEndpoint for handling GitHub webhook events.
//
// Receives push and pull request events from GitHub, validates signatures,
// and queues builds for matching projects.
// =============================================================================

using System.Text;
using System.Text.Json;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.GitHub;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Endpoints.Webhooks;

/// <summary>
/// POST /webhooks/github - Handles GitHub push and pull request webhooks.
/// </summary>
[EnableRateLimiting("webhook")]
public class GitHubWebhookEndpoint : EndpointWithoutRequest<object>
{
    private readonly AndoDbContext _db;
    private readonly GitHubSettings _gitHubSettings;
    private readonly IBuildService _buildService;
    private readonly IProjectService _projectService;
    private readonly ILogger<GitHubWebhookEndpoint> _logger;

    public GitHubWebhookEndpoint(
        AndoDbContext db,
        IOptions<GitHubSettings> gitHubSettings,
        IBuildService buildService,
        IProjectService projectService,
        ILogger<GitHubWebhookEndpoint> logger)
    {
        _db = db;
        _gitHubSettings = gitHubSettings.Value;
        _buildService = buildService;
        _projectService = projectService;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/webhooks/github");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        HttpContext.Request.EnableBuffering();
        using var reader = new StreamReader(HttpContext.Request.Body);
        var body = await reader.ReadToEndAsync(ct);
        HttpContext.Request.Body.Position = 0;

        var signature = HttpContext.Request.Headers["X-Hub-Signature-256"].FirstOrDefault()
            ?? HttpContext.Request.Headers["X-Hub-Signature"].FirstOrDefault();

        var validator = new WebhookSignatureValidator(_gitHubSettings.WebhookSecret);
        if (!validator.Validate(signature, Encoding.UTF8.GetBytes(body)))
        {
            _logger.LogWarning("Invalid webhook signature");
            await SendAsync(new { error = "Invalid signature" }, 401, ct);
            return;
        }

        var eventType = HttpContext.Request.Headers["X-GitHub-Event"].FirstOrDefault();
        var deliveryId = HttpContext.Request.Headers["X-GitHub-Delivery"].FirstOrDefault();

        _logger.LogInformation("Received webhook: {Event} (delivery: {DeliveryId})", eventType, deliveryId);

        var (statusCode, payload) = eventType switch
        {
            "push" => await HandlePushEventAsync(body, ct),
            "pull_request" => await HandlePullRequestEventAsync(body, ct),
            "ping" => (200, (object)new { message = "pong" }),
            _ => (200, (object)new { message = $"Event '{eventType}' ignored" })
        };

        await SendAsync(payload, statusCode, ct);
    }

    private async Task<(int StatusCode, object Payload)> HandlePushEventAsync(string body, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PushEventPayload>(body);
        if (payload?.Repository == null)
        {
            _logger.LogWarning("Invalid push event payload");
            return (400, new { error = "Invalid payload" });
        }

        if (payload.After == "0000000000000000000000000000000000000000")
        {
            _logger.LogInformation("Ignoring branch deletion event");
            return (200, new { message = "Branch deletion ignored" });
        }

        var projects = await _db.Projects
            .Where(p => p.GitHubRepoId == payload.Repository.Id)
            .ToListAsync(ct);

        if (projects.Count == 0)
        {
            _logger.LogInformation("No project configured for repository {Repo}", payload.Repository.FullName);
            return (200, new { message = "Repository not configured" });
        }

        var buildIds = new List<int>();

        foreach (var project in projects)
        {
            if (!project.MatchesBranchFilter(payload.Branch))
            {
                _logger.LogInformation(
                    "Branch {Branch} does not match filter for project {ProjectId}",
                    payload.Branch,
                    project.Id);
                continue;
            }

            await _projectService.DetectAndUpdateRequiredSecretsAsync(project.Id);
            await _db.Entry(project).ReloadAsync(ct);

            var secretNames = await _projectService.GetSecretNamesAsync(project.Id);
            var missingSecrets = project.GetMissingSecretsFrom(secretNames);
            if (missingSecrets.Count > 0)
            {
                _logger.LogWarning(
                    "Project {ProjectId} is missing required secrets: {MissingSecrets}. Skipping build.",
                    project.Id,
                    string.Join(", ", missingSecrets));
                continue;
            }

            if (payload.Installation != null && project.InstallationId != payload.Installation.Id)
            {
                project.InstallationId = payload.Installation.Id;
            }

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
                buildId,
                project.RepoFullName,
                payload.Branch,
                payload.CommitSha[..8]);
        }

        await _db.SaveChangesAsync(ct);

        return buildIds.Count == 0
            ? (200, new { message = "No builds queued (branch filter)" })
            : (200, new { message = $"{buildIds.Count} build(s) queued", buildIds });
    }

    private async Task<(int StatusCode, object Payload)> HandlePullRequestEventAsync(string body, CancellationToken ct)
    {
        var payload = JsonSerializer.Deserialize<PullRequestEventPayload>(body);
        if (payload?.PullRequest?.Head == null || payload.Repository == null)
        {
            _logger.LogWarning("Invalid pull_request event payload");
            return (400, new { error = "Invalid payload" });
        }

        var buildableActions = new[] { "opened", "synchronize", "reopened" };
        if (!buildableActions.Contains(payload.Action))
        {
            _logger.LogInformation("Ignoring PR action: {Action}", payload.Action);
            return (200, new { message = $"Action '{payload.Action}' ignored" });
        }

        var projects = await _db.Projects
            .Where(p => p.GitHubRepoId == payload.Repository.Id)
            .ToListAsync(ct);

        if (projects.Count == 0)
        {
            _logger.LogInformation("No project configured for repository {Repo}", payload.Repository.FullName);
            return (200, new { message = "Repository not configured" });
        }

        var buildIds = new List<int>();

        foreach (var project in projects)
        {
            if (!project.EnablePrBuilds)
            {
                _logger.LogInformation("PR builds not enabled for project {ProjectId}", project.Id);
                continue;
            }

            await _projectService.DetectAndUpdateRequiredSecretsAsync(project.Id);
            await _db.Entry(project).ReloadAsync(ct);

            var secretNames = await _projectService.GetSecretNamesAsync(project.Id);
            var missingSecrets = project.GetMissingSecretsFrom(secretNames);
            if (missingSecrets.Count > 0)
            {
                _logger.LogWarning(
                    "Project {ProjectId} is missing required secrets: {MissingSecrets}. Skipping build.",
                    project.Id,
                    string.Join(", ", missingSecrets));
                continue;
            }

            if (payload.Installation != null && project.InstallationId != payload.Installation.Id)
            {
                project.InstallationId = payload.Installation.Id;
            }

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
                buildId,
                project.RepoFullName,
                payload.Number,
                payload.PullRequest.Head.Sha[..8]);
        }

        await _db.SaveChangesAsync(ct);

        return buildIds.Count == 0
            ? (200, new { message = "No builds queued (PR builds disabled)" })
            : (200, new { message = $"{buildIds.Count} build(s) queued", buildIds });
    }
}
