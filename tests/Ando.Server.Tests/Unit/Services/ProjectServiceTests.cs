// =============================================================================
// ProjectServiceTests.cs
//
// Summary: Unit tests for the ProjectService.
//
// Tests project CRUD operations, settings management, and secret handling.
// Uses in-memory database for isolation.
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ando.Server.Tests.Unit.Services;

public class ProjectServiceTests : IDisposable
{
    private readonly AndoDbContext _db;
    private readonly MockEncryptionService _encryptionService;
    private readonly Mock<IRequiredSecretsDetector> _mockSecretsDetector;
    private readonly Mock<IProfileDetector> _mockProfileDetector;
    private readonly ProjectService _service;

    public ProjectServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _encryptionService = new MockEncryptionService();
        _mockSecretsDetector = new Mock<IRequiredSecretsDetector>();
        _mockProfileDetector = new Mock<IProfileDetector>();

        _service = new ProjectService(
            _db,
            _encryptionService,
            _mockSecretsDetector.Object,
            _mockProfileDetector.Object,
            NullLogger<ProjectService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // -------------------------------------------------------------------------
    // GetProjectsForUserAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProjectsForUserAsync_ReturnsOnlyUserProjects()
    {
        // Arrange
        var user1 = await CreateTestUserAsync("user1");
        var user2 = await CreateTestUserAsync("user2");
        await CreateTestProjectAsync(user1, "repo1");
        await CreateTestProjectAsync(user1, "repo2");
        await CreateTestProjectAsync(user2, "repo3");

        // Act
        var results = await _service.GetProjectsForUserAsync(user1.Id);

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldAllBe(p => p.OwnerId == user1.Id);
    }

    [Fact]
    public async Task GetProjectsForUserAsync_OrdersByLastBuildDescending()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project1 = await CreateTestProjectAsync(user, "repo1");
        var project2 = await CreateTestProjectAsync(user, "repo2");
        var project3 = await CreateTestProjectAsync(user, "repo3");

        // Set different LastBuildAt times
        project1.LastBuildAt = DateTime.UtcNow.AddHours(-3);
        project2.LastBuildAt = DateTime.UtcNow.AddHours(-1); // Most recent
        project3.LastBuildAt = DateTime.UtcNow.AddHours(-2);
        await _db.SaveChangesAsync();

        // Act
        var results = await _service.GetProjectsForUserAsync(user.Id);

        // Assert
        results[0].Id.ShouldBe(project2.Id); // Most recent first
        results[1].Id.ShouldBe(project3.Id);
        results[2].Id.ShouldBe(project1.Id);
    }

    [Fact]
    public async Task GetProjectsForUserAsync_WithNoProjects_ReturnsEmptyList()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var results = await _service.GetProjectsForUserAsync(user.Id);

        // Assert
        results.ShouldBeEmpty();
    }

    // -------------------------------------------------------------------------
    // GetProjectAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProjectAsync_ReturnsProjectWithOwner()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        var result = await _service.GetProjectAsync(project.Id);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(project.Id);
        result.Owner.ShouldNotBeNull();
        result.Owner.Id.ShouldBe(user.Id);
    }

    [Fact]
    public async Task GetProjectAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _service.GetProjectAsync(99999);

        // Assert
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // GetProjectForUserAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProjectForUserAsync_WithOwner_ReturnsProject()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        var result = await _service.GetProjectForUserAsync(project.Id, user.Id);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(project.Id);
    }

    [Fact]
    public async Task GetProjectForUserAsync_WithOtherUser_ReturnsNull()
    {
        // Arrange
        var owner = await CreateTestUserAsync("owner");
        var otherUser = await CreateTestUserAsync("other");
        var project = await CreateTestProjectAsync(owner);

        // Act
        var result = await _service.GetProjectForUserAsync(project.Id, otherUser.Id);

        // Assert
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // CreateProjectAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CreateProjectAsync_SetsOwnerAndRepoDetails()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var project = await _service.CreateProjectAsync(
            user.Id,
            gitHubRepoId: 12345,
            repoFullName: "user/test-repo",
            repoUrl: "https://github.com/user/test-repo",
            defaultBranch: "main",
            installationId: 111);

        // Assert
        project.ShouldNotBeNull();
        project.OwnerId.ShouldBe(user.Id);
        project.GitHubRepoId.ShouldBe(12345);
        project.RepoFullName.ShouldBe("user/test-repo");
        project.RepoUrl.ShouldBe("https://github.com/user/test-repo");
        project.DefaultBranch.ShouldBe("main");
        project.InstallationId.ShouldBe(111);
    }

    [Fact]
    public async Task CreateProjectAsync_SetsBranchFilterToDefaultBranch()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var project = await _service.CreateProjectAsync(
            user.Id,
            gitHubRepoId: 12345,
            repoFullName: "user/test-repo",
            repoUrl: "https://github.com/user/test-repo",
            defaultBranch: "develop");

        // Assert
        project.BranchFilter.ShouldBe("develop");
    }

    [Fact]
    public async Task CreateProjectAsync_SetsCreatedAt()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var before = DateTime.UtcNow;

        // Act
        var project = await _service.CreateProjectAsync(
            user.Id,
            gitHubRepoId: 12345,
            repoFullName: "user/test-repo",
            repoUrl: "https://github.com/user/test-repo",
            defaultBranch: "main");

        // Assert
        project.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
        project.CreatedAt.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
    }

    [Fact]
    public async Task CreateProjectAsync_PersistsToDatabase()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var project = await _service.CreateProjectAsync(
            user.Id,
            gitHubRepoId: 12345,
            repoFullName: "user/test-repo",
            repoUrl: "https://github.com/user/test-repo",
            defaultBranch: "main");

        // Assert - verify it was saved
        var loaded = await _db.Projects.FindAsync(project.Id);
        loaded.ShouldNotBeNull();
        loaded.RepoFullName.ShouldBe("user/test-repo");
    }

    [Fact]
    public async Task CreateProjectAsync_WithDuplicateRepo_ThrowsException()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        await _service.CreateProjectAsync(
            user.Id,
            gitHubRepoId: 12345,
            repoFullName: "user/test-repo",
            repoUrl: "https://github.com/user/test-repo",
            defaultBranch: "main");

        // Act & Assert
        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await _service.CreateProjectAsync(
                user.Id,
                gitHubRepoId: 12345, // Same repo ID
                repoFullName: "user/test-repo",
                repoUrl: "https://github.com/user/test-repo",
                defaultBranch: "main"));
    }

    [Fact]
    public async Task CreateProjectAsync_WithoutInstallationId_SetsNull()
    {
        // Arrange
        var user = await CreateTestUserAsync();

        // Act
        var project = await _service.CreateProjectAsync(
            user.Id,
            gitHubRepoId: 12345,
            repoFullName: "user/test-repo",
            repoUrl: "https://github.com/user/test-repo",
            defaultBranch: "main");
        // installationId not passed

        // Assert
        project.InstallationId.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // UpdateProjectSettingsAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateProjectSettingsAsync_UpdatesAllSettings()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        var result = await _service.UpdateProjectSettingsAsync(
            project.Id,
            branchFilter: "main,develop,feature/*",
            enablePrBuilds: true,
            timeoutMinutes: 30,
            dockerImage: "node:18",
            profile: "deploy",
            notifyOnFailure: true,
            notificationEmail: "dev@example.com");

        // Assert
        result.ShouldBeTrue();
        var updated = await _db.Projects.FindAsync(project.Id);
        updated!.BranchFilter.ShouldBe("main,develop,feature/*");
        updated.EnablePrBuilds.ShouldBeTrue();
        updated.TimeoutMinutes.ShouldBe(30);
        updated.DockerImage.ShouldBe("node:18");
        updated.Profile.ShouldBe("deploy");
        updated.NotifyOnFailure.ShouldBeTrue();
        updated.NotificationEmail.ShouldBe("dev@example.com");
    }

    [Fact]
    public async Task UpdateProjectSettingsAsync_ClampsTimeoutToMinimum()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        await _service.UpdateProjectSettingsAsync(
            project.Id,
            branchFilter: "main",
            enablePrBuilds: false,
            timeoutMinutes: 0, // Below minimum
            dockerImage: null,
            profile: null,
            notifyOnFailure: false,
            notificationEmail: null);

        // Assert
        var updated = await _db.Projects.FindAsync(project.Id);
        updated!.TimeoutMinutes.ShouldBe(1); // Clamped to minimum
    }

    [Fact]
    public async Task UpdateProjectSettingsAsync_ClampsTimeoutToMaximum()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        await _service.UpdateProjectSettingsAsync(
            project.Id,
            branchFilter: "main",
            enablePrBuilds: false,
            timeoutMinutes: 100, // Above maximum
            dockerImage: null,
            profile: null,
            notifyOnFailure: false,
            notificationEmail: null);

        // Assert
        var updated = await _db.Projects.FindAsync(project.Id);
        updated!.TimeoutMinutes.ShouldBe(60); // Clamped to maximum
    }

    [Fact]
    public async Task UpdateProjectSettingsAsync_TrimsWhitespace()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        await _service.UpdateProjectSettingsAsync(
            project.Id,
            branchFilter: "main",
            enablePrBuilds: false,
            timeoutMinutes: 15,
            dockerImage: "  node:18  ",
            profile: null,
            notifyOnFailure: true,
            notificationEmail: "  dev@example.com  ");

        // Assert
        var updated = await _db.Projects.FindAsync(project.Id);
        updated!.DockerImage.ShouldBe("node:18");
        updated.NotificationEmail.ShouldBe("dev@example.com");
    }

    [Fact]
    public async Task UpdateProjectSettingsAsync_WithEmptyStrings_SetsNull()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        project.DockerImage = "node:18";
        project.NotificationEmail = "dev@example.com";
        await _db.SaveChangesAsync();

        // Act
        await _service.UpdateProjectSettingsAsync(
            project.Id,
            branchFilter: "main",
            enablePrBuilds: false,
            timeoutMinutes: 15,
            dockerImage: "   ", // Whitespace only
            profile: null,
            notifyOnFailure: false,
            notificationEmail: "");

        // Assert
        var updated = await _db.Projects.FindAsync(project.Id);
        updated!.DockerImage.ShouldBeNull();
        updated.NotificationEmail.ShouldBeNull();
    }

    [Fact]
    public async Task UpdateProjectSettingsAsync_WithNonExistentProject_ReturnsFalse()
    {
        // Act
        var result = await _service.UpdateProjectSettingsAsync(
            99999,
            branchFilter: "main",
            enablePrBuilds: false,
            timeoutMinutes: 15,
            dockerImage: null,
            profile: null,
            notifyOnFailure: false,
            notificationEmail: null);

        // Assert
        result.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // DeleteProjectAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteProjectAsync_RemovesProject()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        var result = await _service.DeleteProjectAsync(project.Id);

        // Assert
        result.ShouldBeTrue();
        var deleted = await _db.Projects.FindAsync(project.Id);
        deleted.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteProjectAsync_WithNonExistentProject_ReturnsFalse()
    {
        // Act
        var result = await _service.DeleteProjectAsync(99999);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteProjectAsync_CascadesToSecrets()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Add secrets
        await _service.SetSecretAsync(project.Id, "API_KEY", "secret-value");
        await _service.SetSecretAsync(project.Id, "DB_PASSWORD", "password123");

        var secretsBefore = await _db.ProjectSecrets.CountAsync(s => s.ProjectId == project.Id);
        secretsBefore.ShouldBe(2);

        // Act
        await _service.DeleteProjectAsync(project.Id);

        // Assert - secrets should be deleted via cascade
        var secretsAfter = await _db.ProjectSecrets.CountAsync(s => s.ProjectId == project.Id);
        secretsAfter.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // SetSecretAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SetSecretAsync_CreatesNewSecret()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        await _service.SetSecretAsync(project.Id, "API_KEY", "my-secret-value");

        // Assert
        var secret = await _db.ProjectSecrets
            .FirstOrDefaultAsync(s => s.ProjectId == project.Id && s.Name == "API_KEY");
        secret.ShouldNotBeNull();
        secret.EncryptedValue.ShouldNotBe("my-secret-value"); // Should be encrypted
    }

    [Fact]
    public async Task SetSecretAsync_EncryptsValue()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        await _service.SetSecretAsync(project.Id, "API_KEY", "my-secret-value");

        // Assert
        var secret = await _db.ProjectSecrets
            .FirstOrDefaultAsync(s => s.ProjectId == project.Id && s.Name == "API_KEY");

        // MockEncryptionService prefixes with "encrypted:" and base64 encodes
        var decrypted = _encryptionService.Decrypt(secret!.EncryptedValue);
        decrypted.ShouldBe("my-secret-value");
    }

    [Fact]
    public async Task SetSecretAsync_UpdatesExistingSecret()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        await _service.SetSecretAsync(project.Id, "API_KEY", "old-value");

        // Act
        await _service.SetSecretAsync(project.Id, "API_KEY", "new-value");

        // Assert
        var secrets = await _db.ProjectSecrets
            .Where(s => s.ProjectId == project.Id && s.Name == "API_KEY")
            .ToListAsync();
        secrets.Count.ShouldBe(1); // Only one secret, not two

        var decrypted = _encryptionService.Decrypt(secrets[0].EncryptedValue);
        decrypted.ShouldBe("new-value");
    }

    [Fact]
    public async Task SetSecretAsync_SetsCreatedAtForNew()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        var before = DateTime.UtcNow;

        // Act
        await _service.SetSecretAsync(project.Id, "API_KEY", "value");

        // Assert
        var secret = await _db.ProjectSecrets
            .FirstOrDefaultAsync(s => s.ProjectId == project.Id && s.Name == "API_KEY");
        secret!.CreatedAt.ShouldBeGreaterThanOrEqualTo(before);
    }

    [Fact]
    public async Task SetSecretAsync_SetsUpdatedAtForExisting()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        await _service.SetSecretAsync(project.Id, "API_KEY", "old-value");

        var secretBefore = await _db.ProjectSecrets
            .FirstAsync(s => s.ProjectId == project.Id && s.Name == "API_KEY");
        secretBefore.UpdatedAt.ShouldBeNull();

        // Act
        await _service.SetSecretAsync(project.Id, "API_KEY", "new-value");

        // Assert
        var secretAfter = await _db.ProjectSecrets
            .FirstAsync(s => s.ProjectId == project.Id && s.Name == "API_KEY");
        secretAfter.UpdatedAt.ShouldNotBeNull();
    }

    // -------------------------------------------------------------------------
    // DeleteSecretAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteSecretAsync_RemovesSecret()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        await _service.SetSecretAsync(project.Id, "API_KEY", "value");

        // Act
        var result = await _service.DeleteSecretAsync(project.Id, "API_KEY");

        // Assert
        result.ShouldBeTrue();
        var secret = await _db.ProjectSecrets
            .FirstOrDefaultAsync(s => s.ProjectId == project.Id && s.Name == "API_KEY");
        secret.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteSecretAsync_WithNonExistentSecret_ReturnsFalse()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        var result = await _service.DeleteSecretAsync(project.Id, "NONEXISTENT");

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteSecretAsync_OnlyDeletesSpecifiedSecret()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        await _service.SetSecretAsync(project.Id, "API_KEY", "value1");
        await _service.SetSecretAsync(project.Id, "DB_PASSWORD", "value2");

        // Act
        await _service.DeleteSecretAsync(project.Id, "API_KEY");

        // Assert
        var remaining = await _service.GetSecretNamesAsync(project.Id);
        remaining.Count.ShouldBe(1);
        remaining[0].ShouldBe("DB_PASSWORD");
    }

    // -------------------------------------------------------------------------
    // GetSecretNamesAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetSecretNamesAsync_ReturnsAllSecretNames()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        await _service.SetSecretAsync(project.Id, "API_KEY", "value1");
        await _service.SetSecretAsync(project.Id, "DB_PASSWORD", "value2");
        await _service.SetSecretAsync(project.Id, "AWS_SECRET", "value3");

        // Act
        var names = await _service.GetSecretNamesAsync(project.Id);

        // Assert
        names.Count.ShouldBe(3);
        names.ShouldContain("API_KEY");
        names.ShouldContain("DB_PASSWORD");
        names.ShouldContain("AWS_SECRET");
    }

    [Fact]
    public async Task GetSecretNamesAsync_ReturnsSortedNames()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        await _service.SetSecretAsync(project.Id, "ZEBRA", "value1");
        await _service.SetSecretAsync(project.Id, "ALPHA", "value2");
        await _service.SetSecretAsync(project.Id, "MIDDLE", "value3");

        // Act
        var names = await _service.GetSecretNamesAsync(project.Id);

        // Assert
        names[0].ShouldBe("ALPHA");
        names[1].ShouldBe("MIDDLE");
        names[2].ShouldBe("ZEBRA");
    }

    [Fact]
    public async Task GetSecretNamesAsync_WithNoSecrets_ReturnsEmptyList()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);

        // Act
        var names = await _service.GetSecretNamesAsync(project.Id);

        // Assert
        names.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetSecretNamesAsync_DoesNotReturnValues()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        await _service.SetSecretAsync(project.Id, "API_KEY", "super-secret-value");

        // Act
        var names = await _service.GetSecretNamesAsync(project.Id);

        // Assert - only names returned, not values
        names.ShouldContain("API_KEY");
        names.ShouldNotContain("super-secret-value");
    }

    // -------------------------------------------------------------------------
    // DetectAndUpdateProfilesAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task DetectAndUpdateProfilesAsync_ScansDefaultAndFilteredBranches()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        project.DefaultBranch = "main";
        project.BranchFilter = "main,feature/new-profile";
        await _db.SaveChangesAsync();

        _mockProfileDetector
            .Setup(d => d.DetectProfilesAsync(111, project.RepoFullName, "main"))
            .ReturnsAsync(["push"]);
        _mockProfileDetector
            .Setup(d => d.DetectProfilesAsync(111, project.RepoFullName, "feature/new-profile"))
            .ReturnsAsync(["publish"]);

        // Act
        var detected = await _service.DetectAndUpdateProfilesAsync(project.Id);

        // Assert
        detected.Count.ShouldBe(2);
        detected.ShouldContain("publish");
        detected.ShouldContain("push");
        var updated = await _db.Projects.FindAsync(project.Id);
        updated.ShouldNotBeNull();
        updated!.GetAvailableProfileNames().ShouldBe(["publish", "push"]);
        _mockProfileDetector.Verify(
            d => d.DetectProfilesAsync(111, project.RepoFullName, "main"),
            Times.Once);
        _mockProfileDetector.Verify(
            d => d.DetectProfilesAsync(111, project.RepoFullName, "feature/new-profile"),
            Times.Once);
    }

    [Fact]
    public async Task DetectAndUpdateProfilesAsync_SkipsWildcardBranchPatterns()
    {
        // Arrange
        var user = await CreateTestUserAsync();
        var project = await CreateTestProjectAsync(user);
        project.DefaultBranch = "main";
        project.BranchFilter = "main,feature/*";
        await _db.SaveChangesAsync();

        _mockProfileDetector
            .Setup(d => d.DetectProfilesAsync(111, project.RepoFullName, "main"))
            .ReturnsAsync(["publish"]);

        // Act
        var detected = await _service.DetectAndUpdateProfilesAsync(project.Id);

        // Assert
        detected.ShouldBe(["publish"]);
        _mockProfileDetector.Verify(
            d => d.DetectProfilesAsync(111, project.RepoFullName, "main"),
            Times.Once);
        _mockProfileDetector.Verify(
            d => d.DetectProfilesAsync(111, project.RepoFullName, "feature/*"),
            Times.Never);
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

    private async Task<Project> CreateTestProjectAsync(ApplicationUser owner, string repoName = "test-repo")
    {
        var project = new Project
        {
            Owner = owner,
            OwnerId = owner.Id,
            GitHubRepoId = Random.Shared.Next(1, 100000),
            RepoFullName = $"{owner.GitHubLogin}/{repoName}",
            RepoUrl = $"https://github.com/{owner.GitHubLogin}/{repoName}",
            DefaultBranch = "main",
            BranchFilter = "main",
            InstallationId = 111,
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(project);
        await _db.SaveChangesAsync();
        return project;
    }
}
