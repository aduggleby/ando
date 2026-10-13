// =============================================================================
// WebhookIntegrationTestsBase.cs
//
// Summary: Provider-agnostic integration tests for the complete webhook
// processing flow, run once per database provider.
//
// Tests the full pipeline from HTTP request through database state changes,
// using real services except for external dependencies (Hangfire jobs).
// Verifies that webhooks correctly trigger builds, update project state,
// and enqueue background jobs. Concrete subclasses bind the base to the
// SqlServer and PostgreSql collections (see WebhookIntegrationTests.cs).
//
// Design Decisions:
// - Uses WebApplicationFactory for full HTTP pipeline testing
// - Uses a real database via Testcontainers for accurate behavior
// - Mocks only Hangfire to avoid actual job execution
// - Each test class gets isolated database via unique database name
// - The constructor skips (SkippableFact) before touching the fixture's
//   connection string when ANDO_TEST_DB_PROVIDERS deselects the provider
// =============================================================================

using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Webhook integration tests shared by all database providers.
/// </summary>
[Trait("Category", "Integration")]
public abstract class WebhookIntegrationTestsBase : IAsyncLifetime, IDisposable
{
    private readonly DatabaseProvider _provider;
    private readonly AndoWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private IServiceScope? _scope;
    private AndoDbContext? _db;

    /// <summary>
    /// Creates the test server against the fixture's database, or skips the
    /// test when the fixture's provider is not selected.
    /// </summary>
    /// <param name="fixture">Container fixture for the provider under test.</param>
    protected WebhookIntegrationTestsBase(IDatabaseFixture fixture)
    {
        Skip.IfNot(DatabaseTestProviders.IsSelected(fixture.Provider), DatabaseTestProviders.SkipReason(fixture.Provider));

        _provider = fixture.Provider;
        _factory = new AndoWebApplicationFactory(fixture.Provider, fixture.ConnectionString);
        _client = _factory.CreateClient();
    }

    /// <inheritdoc />
    public async Task InitializeAsync()
    {
        await _factory.InitializeDatabaseAsync();
        _scope = _factory.Services.CreateScope();
        _db = _scope.ServiceProvider.GetRequiredService<AndoDbContext>();
    }

    /// <inheritdoc />
    public Task DisposeAsync() => Task.CompletedTask;

    /// <inheritdoc />
    public void Dispose()
    {
        _scope?.Dispose();
        _client.Dispose();
        _factory.Dispose();
    }

    // -------------------------------------------------------------------------
    // Push Webhook Integration Tests
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task PushWebhook_CreatesBuildInDatabase()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var commitSha = "abc123def456789012345678901234567890abcd";
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main", commitSha);

        // Act
        var response = await SendWebhookAsync("push", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldNotBeNull();
        build.CommitSha.ShouldBe(commitSha);
        build.Branch.ShouldBe("main");
        build.Status.ShouldBe(BuildStatus.Queued);
        build.Trigger.ShouldBe(BuildTrigger.Push);
    }

    [SkippableFact]
    public async Task PushWebhook_EnqueuesHangfireJob()
    {
        // Arrange
        var initialJobCount = _factory.EnqueuedJobIds.Count;
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");

        // Act
        var response = await SendWebhookAsync("push", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        _factory.EnqueuedJobIds.Count.ShouldBeGreaterThan(initialJobCount);

        // Verify the build has the job ID
        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldNotBeNull();
        build.HangfireJobId.ShouldNotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task PushWebhook_UpdatesProjectLastBuildAt()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var originalLastBuildAt = project.LastBuildAt;
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");

        // Act
        var response = await SendWebhookAsync("push", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Reload project from database (clear change tracker to get fresh data)
        _db!.ChangeTracker.Clear();
        var updatedProject = await _db!.Projects.FindAsync(project.Id);
        updatedProject!.LastBuildAt.ShouldNotBeNull();
        updatedProject.LastBuildAt!.Value.ShouldBeGreaterThan(originalLastBuildAt ?? DateTime.MinValue);
    }

    [SkippableFact]
    public async Task PushWebhook_UpdatesInstallationId()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        project.InstallationId = 100; // Initial value
        await _db!.SaveChangesAsync();

        var payload = CreatePushPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "main",
            installationId: 999); // Different installation ID

        // Act
        var response = await SendWebhookAsync("push", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // Clear change tracker to get fresh data from database
        _db!.ChangeTracker.Clear();
        var updatedProject = await _db!.Projects.FindAsync(project.Id);
        updatedProject!.InstallationId.ShouldBe(999);
    }

    [SkippableFact]
    public async Task PushWebhook_StoresCommitMetadata()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var commitMessage = "Fix critical bug in authentication";
        var commitAuthor = "Jane Developer";
        var payload = CreatePushPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "main",
            commitMessage: commitMessage,
            commitAuthor: commitAuthor);

        // Act
        var response = await SendWebhookAsync("push", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldNotBeNull();
        build.CommitMessage.ShouldBe(commitMessage);
        build.CommitAuthor.ShouldBe(commitAuthor);
    }

    [SkippableFact]
    public async Task PushWebhook_WithBranchNotInFilter_DoesNotCreateBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(branchFilter: "main,master");
        var payload = CreatePushPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "feature/experimental");

        // Act
        var response = await SendWebhookAsync("push", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldBeNull();
    }

    [SkippableFact]
    public async Task PushWebhook_ForUnknownRepository_ReturnsOkButNoBuild()
    {
        // Arrange
        var unknownRepoId = 999999L;
        var buildCountBefore = await _db!.Builds.CountAsync();
        var payload = CreatePushPayload(unknownRepoId, "unknown/repo", "main");

        // Act
        var response = await SendWebhookAsync("push", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        // No new builds should have been created
        var buildCountAfter = await _db!.Builds.CountAsync();
        buildCountAfter.ShouldBe(buildCountBefore);
    }

    [SkippableFact]
    public async Task PushWebhook_BranchDeletion_DoesNotCreateBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "feature/deleted",
            commitSha: "0000000000000000000000000000000000000000"); // All zeros = branch deletion

        // Act
        var response = await SendWebhookAsync("push", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Pull Request Webhook Integration Tests
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task PullRequestWebhook_WithPrBuildsEnabled_CreatesBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: true);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "opened",
            prNumber: 42);

        // Act
        var response = await SendWebhookAsync("pull_request", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldNotBeNull();
        build.Trigger.ShouldBe(BuildTrigger.PullRequest);
        build.PullRequestNumber.ShouldBe(42);
    }

    [SkippableFact]
    public async Task PullRequestWebhook_WithPrBuildsDisabled_DoesNotCreateBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: false);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "opened",
            prNumber: 42);

        // Act
        var response = await SendWebhookAsync("pull_request", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldBeNull();
    }

    [SkippableFact]
    public async Task PullRequestWebhook_Synchronize_CreatesBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: true);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "synchronize",
            prNumber: 42);

        // Act
        var response = await SendWebhookAsync("pull_request", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldNotBeNull();
    }

    [SkippableFact]
    public async Task PullRequestWebhook_Closed_DoesNotCreateBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: true);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "closed",
            prNumber: 42);

        // Act
        var response = await SendWebhookAsync("pull_request", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldBeNull();
    }

    [SkippableFact]
    public async Task PullRequestWebhook_StoresPrTitle()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: true);
        var prTitle = "Add new authentication feature";
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "opened",
            prNumber: 42,
            prTitle: prTitle);

        // Act
        var response = await SendWebhookAsync("pull_request", payload);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldNotBeNull();
        build.CommitMessage.ShouldNotBeNull();
        build.CommitMessage.ShouldContain(prTitle);
        build.CommitMessage.ShouldContain("PR #42");
    }

    // -------------------------------------------------------------------------
    // Security Tests
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task Webhook_WithInvalidSignature_ReturnsUnauthorized()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");

        // Act - send with invalid signature
        var response = await SendWebhookAsync("push", payload, invalidSignature: true);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);

        var build = await _db!.Builds.FirstOrDefaultAsync(b => b.ProjectId == project.Id);
        build.ShouldBeNull();
    }

    [SkippableFact]
    public async Task Webhook_WithMissingSignature_ReturnsUnauthorized()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");

        // Act - send without signature header
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/github")
        {
            Content = content
        };
        request.Headers.Add("X-GitHub-Event", "push");
        // Note: Not adding X-Hub-Signature-256

        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // -------------------------------------------------------------------------
    // Multi-Webhook Tests
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task MultipleWebhooks_CreateMultipleBuilds()
    {
        // Arrange
        var project = await CreateTestProjectAsync();

        var payloads = new[]
        {
            CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main",
                "commit1111111111111111111111111111111111"),
            CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main",
                "commit2222222222222222222222222222222222"),
            CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main",
                "commit3333333333333333333333333333333333")
        };

        // Act
        foreach (var payload in payloads)
        {
            await SendWebhookAsync("push", payload);
        }

        // Assert
        var builds = await _db!.Builds.Where(b => b.ProjectId == project.Id).ToListAsync();
        builds.Count.ShouldBe(3);

        builds.Select(b => b.CommitSha).ShouldBe(
            new[]
            {
                "commit1111111111111111111111111111111111",
                "commit2222222222222222222222222222222222",
                "commit3333333333333333333333333333333333"
            },
            ignoreOrder: true);
    }

    [SkippableFact]
    public async Task WebhooksForDifferentProjects_CreateSeparateBuilds()
    {
        // Arrange
        var project1 = await CreateTestProjectAsync();
        var project2 = await CreateTestProjectAsync();

        var payload1 = CreatePushPayload(project1.GitHubRepoId, project1.RepoFullName, "main");
        var payload2 = CreatePushPayload(project2.GitHubRepoId, project2.RepoFullName, "main");

        // Act
        await SendWebhookAsync("push", payload1);
        await SendWebhookAsync("push", payload2);

        // Assert
        var builds1 = await _db!.Builds.Where(b => b.ProjectId == project1.Id).ToListAsync();
        var builds2 = await _db!.Builds.Where(b => b.ProjectId == project2.Id).ToListAsync();

        builds1.Count.ShouldBe(1);
        builds2.Count.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Response Format Tests
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task PushWebhook_ReturnsJsonWithBuildIds()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");

        // Act
        var response = await SendWebhookAsync("push", payload);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonDocument.Parse(content);
        json.RootElement.TryGetProperty("buildIds", out var buildIdsElement).ShouldBeTrue();
        buildIdsElement.GetArrayLength().ShouldBeGreaterThan(0);
        buildIdsElement[0].GetInt32().ShouldBeGreaterThan(0);
    }

    [SkippableFact]
    public async Task PingEvent_ReturnsPong()
    {
        // Arrange
        var payload = "{\"zen\":\"Keep it logically awesome.\",\"hook_id\":12345}";

        // Act
        var response = await SendWebhookAsync("ping", payload);
        var content = await response.Content.ReadAsStringAsync();

        // Assert
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        content.ShouldContain("pong");
    }

    // -------------------------------------------------------------------------
    // Health
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task Health_ReportsProviderUnderTest()
    {
        // Act
        var response = await _client.GetAsync("/health");
        var content = await response.Content.ReadAsStringAsync();

        // Assert - the server talks to the real database and names its provider
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var json = JsonDocument.Parse(content);
        json.RootElement.GetProperty("status").GetString().ShouldBe("healthy");
        json.RootElement.GetProperty("database").GetString().ShouldBe(_provider.ToString());
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Project> CreateTestProjectAsync(
        string branchFilter = "main",
        bool enablePrBuilds = false)
    {
        var user = new ApplicationUser
        {
            GitHubId = Random.Shared.Next(1, 100000),
            GitHubLogin = $"testuser_{Guid.NewGuid():N}",
            CreatedAt = DateTime.UtcNow
        };
        _db!.Users.Add(user);

        var project = new Project
        {
            Owner = user,
            GitHubRepoId = Random.Shared.Next(1, 100000),
            RepoFullName = $"{user.GitHubLogin}/test-repo",
            RepoUrl = $"https://github.com/{user.GitHubLogin}/test-repo",
            DefaultBranch = "main",
            BranchFilter = branchFilter,
            EnablePrBuilds = enablePrBuilds,
            InstallationId = 111,
            TimeoutMinutes = 15,
            CreatedAt = DateTime.UtcNow
        };
        _db!.Projects.Add(project);

        await _db!.SaveChangesAsync();
        return project;
    }

    private async Task<HttpResponseMessage> SendWebhookAsync(
        string eventType,
        string payload,
        bool invalidSignature = false)
    {
        var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(HttpMethod.Post, "/webhooks/github")
        {
            Content = content
        };

        // Add GitHub event header
        request.Headers.Add("X-GitHub-Event", eventType);
        request.Headers.Add("X-GitHub-Delivery", Guid.NewGuid().ToString());

        // Compute signature
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        string signature;
        if (invalidSignature)
        {
            signature = "sha256=invalid0000000000000000000000000000000000000000000000000000";
        }
        else
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_factory.WebhookSecret));
            var hash = hmac.ComputeHash(payloadBytes);
            signature = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        }
        request.Headers.Add("X-Hub-Signature-256", signature);

        return await _client.SendAsync(request);
    }

    private string CreatePushPayload(
        long repoId,
        string repoFullName,
        string branch,
        string? commitSha = null,
        string? commitMessage = null,
        string? commitAuthor = null,
        long? installationId = null)
    {
        commitSha ??= Guid.NewGuid().ToString("N") + "12345678";
        commitMessage ??= "Test commit";
        commitAuthor ??= "Test Author";
        installationId ??= 111;

        return $$"""
        {
            "ref": "refs/heads/{{branch}}",
            "after": "{{commitSha}}",
            "repository": {
                "id": {{repoId}},
                "full_name": "{{repoFullName}}"
            },
            "head_commit": {
                "id": "{{commitSha}}",
                "message": "{{commitMessage}}",
                "author": {
                    "name": "{{commitAuthor}}"
                }
            },
            "installation": {
                "id": {{installationId}}
            }
        }
        """;
    }

    private string CreatePullRequestPayload(
        long repoId,
        string repoFullName,
        string action,
        int prNumber,
        string? prTitle = null,
        string? targetBranch = null,
        long? installationId = null)
    {
        prTitle ??= "Test PR";
        targetBranch ??= "main";
        installationId ??= 111;
        var headSha = Guid.NewGuid().ToString("N") + "12345678";

        return $$"""
        {
            "action": "{{action}}",
            "number": {{prNumber}},
            "pull_request": {
                "number": {{prNumber}},
                "head": {
                    "sha": "{{headSha}}",
                    "ref": "feature/test-branch"
                },
                "base": {
                    "ref": "{{targetBranch}}"
                },
                "title": "{{prTitle}}",
                "user": {
                    "login": "contributor"
                }
            },
            "repository": {
                "id": {{repoId}},
                "full_name": "{{repoFullName}}"
            },
            "installation": {
                "id": {{installationId}}
            }
        }
        """;
    }
}
