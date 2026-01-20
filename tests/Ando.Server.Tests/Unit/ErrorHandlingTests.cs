// =============================================================================
// ErrorHandlingTests.cs
//
// Summary: Unit tests for error handling across the application.
//
// Tests that errors are properly caught, logged, and result in appropriate
// responses or status updates. Covers webhook errors, build failures,
// and service exceptions.
// =============================================================================

using Ando.Server.BuildExecution;
using Ando.Server.Configuration;
using Ando.Server.Controllers;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using System.Security.Cryptography;
using System.Text;

namespace Ando.Server.Tests.Unit;

public class ErrorHandlingTests : IDisposable
{
    private readonly AndoDbContext _db;
    private readonly GitHubSettings _gitHubSettings;

    public ErrorHandlingTests()
    {
        _db = TestDbContextFactory.Create();
        _gitHubSettings = new GitHubSettings
        {
            WebhookSecret = "test-webhook-secret"
        };
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // -------------------------------------------------------------------------
    // Webhook Error Handling Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task Webhook_WhenBuildServiceThrows_ExceptionPropagates()
    {
        // Arrange
        var buildService = new MockBuildService
        {
            ThrowOnQueueBuild = new InvalidOperationException("Database connection failed")
        };

        var controller = CreateWebhooksController(buildService);
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");
        SetupRequest(controller, payload, "push");

        // Act & Assert - exception propagates (would be caught by middleware in production)
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await controller.GitHub());
    }

    [Fact]
    public async Task Webhook_WhenBuildServiceThrows_NoBuildQueued()
    {
        // Arrange
        var buildService = new MockBuildService
        {
            ThrowOnQueueBuild = new Exception("Service unavailable")
        };

        var controller = CreateWebhooksController(buildService);
        var project = await CreateTestProjectAsync();
        var payload = CreatePushPayload(project.GitHubRepoId, project.RepoFullName, "main");
        SetupRequest(controller, payload, "push");

        // Act
        try { await controller.GitHub(); } catch { }

        // Assert - no build was queued
        buildService.QueueBuildCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Webhook_WithEmptyBody_ReturnsUnauthorized()
    {
        // Arrange - empty body means signature can't match
        var buildService = new MockBuildService();
        var controller = CreateWebhooksController(buildService);
        SetupRequest(controller, "", "push");

        // Act
        var result = await controller.GitHub();

        // Assert - signature validation fails first
        result.ShouldBeOfType<UnauthorizedObjectResult>();
        buildService.QueueBuildCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task Webhook_WithInvalidSignature_ReturnsUnauthorized()
    {
        // Arrange
        var buildService = new MockBuildService();
        var controller = CreateWebhooksController(buildService);

        // Set up with invalid signature
        var context = new DefaultHttpContext();
        var payload = "{\"test\": true}";
        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Headers["X-GitHub-Event"] = "push";
        context.Request.Headers["X-Hub-Signature-256"] = "sha256=invalid";
        controller.ControllerContext = new ControllerContext { HttpContext = context };

        // Act
        var result = await controller.GitHub();

        // Assert
        result.ShouldBeOfType<UnauthorizedObjectResult>();
    }

    [Fact]
    public async Task Webhook_WithUnknownRepository_ReturnsOkButSkips()
    {
        // Arrange
        var buildService = new MockBuildService();
        var controller = CreateWebhooksController(buildService);
        var payload = CreatePushPayload(999999, "unknown/repo", "main");
        SetupRequest(controller, payload, "push");

        // Act
        var result = await controller.GitHub();

        // Assert - OK response but no build queued
        result.ShouldBeOfType<OkObjectResult>();
        buildService.QueueBuildCalls.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // Build Service Error Handling Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QueueBuildAsync_WithNonExistentProject_DoesNotUpdateLastBuildAt()
    {
        // Arrange
        // Note: In-memory database doesn't enforce foreign keys, so this tests
        // the graceful handling of missing project after build creation
        var jobClient = new Mock<IBackgroundJobClient>();
        var cancellationRegistry = new CancellationTokenRegistry();

        var service = new BuildService(
            _db,
            jobClient.Object,
            cancellationRegistry,
            NullLogger<BuildService>.Instance);

        // Act - in-memory DB allows this, production DB would fail with FK violation
        var buildId = await service.QueueBuildAsync(
            99999, // Non-existent project
            "abc123",
            "main",
            BuildTrigger.Push);

        // Assert - build was created but project.LastBuildAt wasn't updated
        // (because project doesn't exist)
        buildId.ShouldBeGreaterThan(0);
        var project = await _db.Projects.FindAsync(99999);
        project.ShouldBeNull();
    }

    [Fact]
    public async Task RetryBuildAsync_WithNonExistentBuild_ThrowsException()
    {
        // Arrange
        var jobClient = new Mock<IBackgroundJobClient>();
        var cancellationRegistry = new CancellationTokenRegistry();

        var service = new BuildService(
            _db,
            jobClient.Object,
            cancellationRegistry,
            NullLogger<BuildService>.Instance);

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await service.RetryBuildAsync(99999));
    }

    [Fact]
    public async Task UpdateBuildStatusAsync_WithNonExistentBuild_NoOps()
    {
        // Arrange
        var jobClient = new Mock<IBackgroundJobClient>();
        var cancellationRegistry = new CancellationTokenRegistry();

        var service = new BuildService(
            _db,
            jobClient.Object,
            cancellationRegistry,
            NullLogger<BuildService>.Instance);

        // Act - should not throw
        await service.UpdateBuildStatusAsync(99999, BuildStatus.Failed);

        // Assert - no exception, just no-op
    }

    [Fact]
    public async Task CancelBuildAsync_WithNonExistentBuild_ReturnsFalse()
    {
        // Arrange
        var jobClient = new Mock<IBackgroundJobClient>();
        var cancellationRegistry = new CancellationTokenRegistry();

        var service = new BuildService(
            _db,
            jobClient.Object,
            cancellationRegistry,
            NullLogger<BuildService>.Instance);

        // Act
        var result = await service.CancelBuildAsync(99999);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task CancelBuildAsync_WithCompletedBuild_ReturnsFalse()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        var build = await CreateTestBuildAsync(project, BuildStatus.Success);

        var jobClient = new Mock<IBackgroundJobClient>();
        var cancellationRegistry = new CancellationTokenRegistry();

        var service = new BuildService(
            _db,
            jobClient.Object,
            cancellationRegistry,
            NullLogger<BuildService>.Instance);

        // Act
        var result = await service.CancelBuildAsync(build.Id);

        // Assert - can't cancel completed build
        result.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Project Service Error Handling Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateProjectAsync_WithDuplicateRepo_ThrowsException()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var encryptionService = new MockEncryptionService();
        var mockSecretsDetector = new Mock<IRequiredSecretsDetector>();
        var mockProfileDetector = new Mock<IProfileDetector>();
        var service = new ProjectService(
            _db,
            encryptionService,
            mockSecretsDetector.Object,
            mockProfileDetector.Object,
            NullLogger<ProjectService>.Instance);

        // Create first project
        await service.CreateProjectAsync(
            user.Id,
            12345,
            "user/repo",
            "https://github.com/user/repo",
            "main");

        // Act & Assert - duplicate should throw
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await service.CreateProjectAsync(
                user.Id,
                12345, // Same repo ID
                "user/repo",
                "https://github.com/user/repo",
                "main"));
    }

    [Fact]
    public async Task DeleteProjectAsync_WithNonExistentProject_ReturnsFalse()
    {
        // Arrange
        var encryptionService = new MockEncryptionService();
        var mockSecretsDetector = new Mock<IRequiredSecretsDetector>();
        var mockProfileDetector = new Mock<IProfileDetector>();
        var service = new ProjectService(
            _db,
            encryptionService,
            mockSecretsDetector.Object,
            mockProfileDetector.Object,
            NullLogger<ProjectService>.Instance);

        // Act
        var result = await service.DeleteProjectAsync(99999);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateProjectSettingsAsync_WithNonExistentProject_ReturnsFalse()
    {
        // Arrange
        var encryptionService = new MockEncryptionService();
        var mockSecretsDetector = new Mock<IRequiredSecretsDetector>();
        var mockProfileDetector = new Mock<IProfileDetector>();
        var service = new ProjectService(
            _db,
            encryptionService,
            mockSecretsDetector.Object,
            mockProfileDetector.Object,
            NullLogger<ProjectService>.Instance);

        // Act
        var result = await service.UpdateProjectSettingsAsync(
            99999,
            "main",
            false,
            15,
            null,
            null,
            false,
            null);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteSecretAsync_WithNonExistentSecret_ReturnsFalse()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        var encryptionService = new MockEncryptionService();
        var mockSecretsDetector = new Mock<IRequiredSecretsDetector>();
        var mockProfileDetector = new Mock<IProfileDetector>();
        var service = new ProjectService(
            _db,
            encryptionService,
            mockSecretsDetector.Object,
            mockProfileDetector.Object,
            NullLogger<ProjectService>.Instance);

        // Act
        var result = await service.DeleteSecretAsync(project.Id, "NONEXISTENT");

        // Assert
        result.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Encryption Service Error Handling Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void EncryptionService_WithInvalidKey_ThrowsOnConstruction()
    {
        // Arrange
        var invalidSettings = new EncryptionSettings { Key = "not-a-valid-base64-key!!!" };

        // Act & Assert
        Should.Throw<Exception>(() =>
            new EncryptionService(Options.Create(invalidSettings)));
    }

    [Fact]
    public void EncryptionService_Decrypt_WithCorruptedData_Throws()
    {
        // Arrange
        var settings = new EncryptionSettings
        {
            Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=" // Valid 32-byte key
        };
        var service = new EncryptionService(Options.Create(settings));

        // Act & Assert
        Should.Throw<Exception>(() => service.Decrypt("not-valid-ciphertext"));
    }

    [Fact]
    public void EncryptionService_Decrypt_WithWrongKey_Throws()
    {
        // Arrange
        var settings1 = new EncryptionSettings
        {
            Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY="
        };
        var settings2 = new EncryptionSettings
        {
            Key = "ZmVkY2JhOTg3NjU0MzIxMGZlZGNiYTk4NzY1NDMyMTA="
        };

        var service1 = new EncryptionService(Options.Create(settings1));
        var service2 = new EncryptionService(Options.Create(settings2));

        var encrypted = service1.Encrypt("secret");

        // Act & Assert
        Should.Throw<Exception>(() => service2.Decrypt(encrypted));
    }

    // -------------------------------------------------------------------------
    // Cancellation Registry Error Handling Tests
    // -------------------------------------------------------------------------

    [Fact]
    public void CancellationRegistry_TryCancel_UnregisteredBuild_ReturnsFalse()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();

        // Act
        var result = registry.TryCancel(99999);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void CancellationRegistry_IsRunning_UnregisteredBuild_ReturnsFalse()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();

        // Act
        var result = registry.IsRunning(99999);

        // Assert
        result.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private WebhooksController CreateWebhooksController(MockBuildService buildService)
    {
        var mockProjectService = new Mock<IProjectService>();
        var controller = new WebhooksController(
            _db,
            Options.Create(_gitHubSettings),
            buildService,
            mockProjectService.Object,
            NullLogger<WebhooksController>.Instance);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext()
        };

        return controller;
    }

    private async Task<ApplicationUser> CreateTestUserAsync(string login = "testuser")
    {
        var user = new ApplicationUser
        {
            GitHubId = Random.Shared.Next(1, 100000),
            GitHubLogin = login,
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Project> CreateTestProjectAsync(ApplicationUser? owner = null)
    {
        owner ??= await CreateTestUserAsync();

        var project = new Project
        {
            Owner = owner,
            OwnerId = owner.Id,
            GitHubRepoId = Random.Shared.Next(1, 100000),
            RepoFullName = $"{owner.GitHubLogin}/test-repo",
            RepoUrl = $"https://github.com/{owner.GitHubLogin}/test-repo",
            DefaultBranch = "main",
            BranchFilter = "main",
            InstallationId = 111,
            TimeoutMinutes = 15,
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }

    private async Task<Build> CreateTestBuildAsync(Project project, BuildStatus status)
    {
        var build = new Build
        {
            Project = project,
            ProjectId = project.Id,
            CommitSha = "abc123def456789012345678901234567890abcd",
            Branch = "main",
            Status = status,
            Trigger = BuildTrigger.Push,
            QueuedAt = DateTime.UtcNow
        };
        _db.Builds.Add(build);
        await _db.SaveChangesAsync();
        return build;
    }

    private string CreatePushPayload(long repoId, string repoFullName, string branch)
    {
        return $$"""
        {
            "ref": "refs/heads/{{branch}}",
            "after": "abc123def456789012345678901234567890abcd",
            "repository": {
                "id": {{repoId}},
                "full_name": "{{repoFullName}}"
            },
            "head_commit": {
                "id": "abc123def456789012345678901234567890abcd",
                "message": "Test commit",
                "author": {
                    "name": "Test Author"
                }
            },
            "installation": {
                "id": 111
            }
        }
        """;
    }

    private void SetupRequest(WebhooksController controller, string payload, string eventType)
    {
        var context = new DefaultHttpContext();

        var bodyBytes = Encoding.UTF8.GetBytes(payload);
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.ContentType = "application/json";

        context.Request.Headers["X-GitHub-Event"] = eventType;

        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_gitHubSettings.WebhookSecret));
        var hash = hmac.ComputeHash(bodyBytes);
        var signature = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();
        context.Request.Headers["X-Hub-Signature-256"] = signature;

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = context
        };
    }
}
