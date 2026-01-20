// =============================================================================
// WebhooksControllerTests.cs
//
// Summary: Unit tests for the GitHub webhooks controller.
//
// Tests webhook payload processing including:
// - Push event handling (triggers build queue)
// - Pull request event handling
// - Branch filtering
// - Signature validation integration
// - Error handling
// =============================================================================

using Ando.Server.Controllers;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace Ando.Server.Tests.Unit;

public class WebhooksControllerTests : IDisposable
{
    private readonly AndoDbContext _db;
    private readonly MockBuildService _buildService;
    private readonly WebhooksController _controller;
    private readonly GitHubSettings _settings;

    public WebhooksControllerTests()
    {
        _db = TestDbContextFactory.Create();
        _buildService = new MockBuildService();
        _settings = new GitHubSettings
        {
            WebhookSecret = "test-webhook-secret"
        };

        var mockProjectService = new Mock<IProjectService>();
        _controller = new WebhooksController(
            _db,
            Options.Create(_settings),
            _buildService,
            mockProjectService.Object,
            NullLogger<WebhooksController>.Instance);

        // Set up default HTTP context
        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // -------------------------------------------------------------------------
    // Push Event Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Push_WithValidSignature_QueuesBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");
        SetupRequest(payload, "push");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldHaveSingleItem();

        var call = _buildService.QueueBuildCalls[0];
        call.ProjectId.ShouldBe(project.Id);
        call.Branch.ShouldBe("main");
        call.Trigger.ShouldBe(BuildTrigger.Push);
    }

    [Fact]
    public async Task Push_InitiatesRepoDownloadViaBuildQueue()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var commitSha = "abc123def456789012345678901234567890abcd";
        var payload = CreatePushPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "main",
            commitSha: commitSha);
        SetupRequest(payload, "push");

        // Act
        var result = await _controller.GitHub();

        // Assert
        // The build service was called, which will trigger the BuildOrchestrator
        // The orchestrator will call GitHubService.CloneRepositoryAsync
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldHaveSingleItem();

        var call = _buildService.QueueBuildCalls[0];
        call.CommitSha.ShouldBe(commitSha);

        // This verifies that the webhook correctly passes the commit SHA
        // which is essential for the repo download
    }

    [Fact]
    public async Task Push_WithBranchNotInFilter_SkipsBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(branchFilter: "main,master");
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "feature/test");
        SetupRequest(payload, "push");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Push_WithBranchInFilter_QueuesBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(branchFilter: "main,develop,feature/*");
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "develop");
        SetupRequest(payload, "push");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Push_WithExactBranchMatch_QueuesBuild()
    {
        // Arrange - tests exact branch matching (no wildcard support currently)
        var project = await CreateTestProjectAsync(branchFilter: "main,feature/new-feature");
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "feature/new-feature");
        SetupRequest(payload, "push");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task Push_WithInvalidSignature_ReturnsUnauthorized()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");
        SetupRequest(payload, "push", invalidSignature: true);

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<UnauthorizedObjectResult>();
        _buildService.QueueBuildCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Push_WithUnknownRepo_ReturnsOkButSkipsBuild()
    {
        // Arrange
        var payload = CreatePushPayload(999999, "unknown/repo", "main");
        SetupRequest(payload, "push");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Push_IncludesCommitMessageAndAuthor()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "main",
            commitMessage: "Fix bug in authentication",
            commitAuthor: "John Doe");
        SetupRequest(payload, "push");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        var call = _buildService.QueueBuildCalls.ShouldHaveSingleItem();
        call.CommitMessage.ShouldBe("Fix bug in authentication");
        call.CommitAuthor.ShouldBe("John Doe");
    }

    // -------------------------------------------------------------------------
    // Pull Request Event Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task PullRequest_WithPrBuildsEnabled_QueuesBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: true);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "opened",
            prNumber: 42);
        SetupRequest(payload, "pull_request");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        var call = _buildService.QueueBuildCalls.ShouldHaveSingleItem();
        call.Trigger.ShouldBe(BuildTrigger.PullRequest);
        call.PullRequestNumber.ShouldBe(42);
    }

    [Fact]
    public async Task PullRequest_WithPrBuildsDisabled_SkipsBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: false);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "opened",
            prNumber: 42);
        SetupRequest(payload, "pull_request");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task PullRequest_Synchronize_QueuesBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: true);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "synchronize",
            prNumber: 42);
        SetupRequest(payload, "pull_request");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task PullRequest_Reopened_QueuesBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: true);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "reopened",
            prNumber: 42);
        SetupRequest(payload, "pull_request");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task PullRequest_Closed_SkipsBuild()
    {
        // Arrange
        var project = await CreateTestProjectAsync(enablePrBuilds: true);
        var payload = CreatePullRequestPayload(
            project.GitHubRepoId,
            project.RepoFullName,
            "closed",
            prNumber: 42);
        SetupRequest(payload, "pull_request");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Other Event Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Ping_ReturnsOk()
    {
        // Arrange
        var payload = "{\"zen\":\"Keep it logically awesome.\"}";
        SetupRequest(payload, "ping");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
    }

    [Fact]
    public async Task UnknownEvent_ReturnsOk()
    {
        // Arrange
        var payload = "{}";
        SetupRequest(payload, "unknown_event");

        // Act
        var result = await _controller.GitHub();

        // Assert
        result.ShouldBeOfType<OkObjectResult>();
        _buildService.QueueBuildCalls.ShouldBeEmpty();
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
            GitHubId = 12345,
            GitHubLogin = "testuser",
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);

        var project = new Project
        {
            Owner = user,
            GitHubRepoId = 67890,
            RepoFullName = "testuser/test-repo",
            RepoUrl = "https://github.com/testuser/test-repo",
            DefaultBranch = "main",
            BranchFilter = branchFilter,
            EnablePrBuilds = enablePrBuilds,
            InstallationId = 111,
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(project);

        await _db.SaveChangesAsync();
        return project;
    }

    private string CreatePushPayload(
        long repoId,
        string repoFullName,
        string branch,
        string? commitSha = null,
        string? commitMessage = null,
        string? commitAuthor = null)
    {
        commitSha ??= "abc123def456789012345678901234567890abcd";
        commitMessage ??= "Test commit";
        commitAuthor ??= "Test Author";

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
                "id": 111
            }
        }
        """;
    }

    private string CreatePullRequestPayload(
        long repoId,
        string repoFullName,
        string action,
        int prNumber,
        string? targetBranch = null)
    {
        targetBranch ??= "main";
        var headSha = "def456789012345678901234567890abcdef12";

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
                "title": "Test PR",
                "user": {
                    "login": "contributor"
                }
            },
            "repository": {
                "id": {{repoId}},
                "full_name": "{{repoFullName}}"
            },
            "installation": {
                "id": 111
            }
        }
        """;
    }

    private void SetupRequest(string payload, string eventType, bool invalidSignature = false)
    {
        var context = new DefaultHttpContext();

        // Set up the request body
        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.ContentType = "application/json";

        // Set up headers
        context.Request.Headers["X-GitHub-Event"] = eventType;

        // Compute signature
        string signature;
        if (invalidSignature)
        {
            signature = "sha256=invalid0000000000000000000000000000000000000000000000000000";
        }
        else
        {
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_settings.WebhookSecret));
            var hash = hmac.ComputeHash(bodyBytes);
            signature = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        }
        context.Request.Headers["X-Hub-Signature-256"] = signature;

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = context
        };
    }
}
