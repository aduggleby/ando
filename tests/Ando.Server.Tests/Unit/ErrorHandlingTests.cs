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
using Ando.Server.Data;
using Ando.Server.Hubs;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Hangfire;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

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
            CreateNoOpHubContext(),
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
            CreateNoOpHubContext(),
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
            CreateNoOpHubContext(),
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
            CreateNoOpHubContext(),
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
            CreateNoOpHubContext(),
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

    private static IHubContext<BuildLogHub> CreateNoOpHubContext()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(x => x.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var hubClients = new Mock<IHubClients>();
        hubClients.Setup(x => x.Group(It.IsAny<string>())).Returns(clientProxy.Object);
        hubClients.Setup(x => x.All).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<BuildLogHub>>();
        hubContext.Setup(x => x.Clients).Returns(hubClients.Object);
        return hubContext.Object;
    }
}
