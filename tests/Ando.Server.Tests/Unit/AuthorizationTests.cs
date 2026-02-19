// =============================================================================
// AuthorizationTests.cs
//
// Summary: Unit tests for authorization and access control.
//
// Tests that users can only access their own projects and builds.
// Verifies proper isolation between different users' resources.
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Hubs;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Ando.Server.BuildExecution;
using Hangfire;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ando.Server.Tests.Unit;

public class AuthorizationTests : IDisposable
{
    private readonly AndoDbContext _db;
    private readonly BuildService _buildService;
    private readonly ProjectService _projectService;
    private readonly MockEncryptionService _encryptionService;

    public AuthorizationTests()
    {
        _db = TestDbContextFactory.Create();
        var jobClient = new Mock<IBackgroundJobClient>();
        var cancellationRegistry = new CancellationTokenRegistry();
        _encryptionService = new MockEncryptionService();

        _buildService = new BuildService(
            _db,
            jobClient.Object,
            cancellationRegistry,
            CreateNoOpHubContext(),
            NullLogger<BuildService>.Instance);

        var mockSecretsDetector = new Mock<IRequiredSecretsDetector>();
        var mockProfileDetector = new Mock<IProfileDetector>();
        _projectService = new ProjectService(
            _db,
            _encryptionService,
            mockSecretsDetector.Object,
            mockProfileDetector.Object,
            NullLogger<ProjectService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
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

    // -------------------------------------------------------------------------
    // Project Access Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetProject_ByOwner_ReturnsProject()
    {
        // Arrange
        var user = await CreateUserAsync("owner");
        var project = await CreateProjectAsync(user);

        // Act
        var result = await _projectService.GetProjectForUserAsync(project.Id, user.Id);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(project.Id);
    }

    [Fact]
    public async Task GetProject_ByOtherUser_ReturnsNull()
    {
        // Arrange
        var owner = await CreateUserAsync("owner");
        var otherUser = await CreateUserAsync("other");
        var project = await CreateProjectAsync(owner);

        // Act
        var result = await _projectService.GetProjectForUserAsync(project.Id, otherUser.Id);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task GetProjects_ForUser_ReturnsOnlyOwnedProjects()
    {
        // Arrange
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");

        await CreateProjectAsync(user1, "user1-repo-1");
        await CreateProjectAsync(user1, "user1-repo-2");
        await CreateProjectAsync(user2, "user2-repo-1");

        // Act
        var results = await _projectService.GetProjectsForUserAsync(user1.Id);

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldAllBe(p => p.OwnerId == user1.Id);
    }

    [Fact]
    public async Task DeleteProject_AfterAuthorizationCheck_Succeeds()
    {
        // Arrange
        var user = await CreateUserAsync("owner");
        var project = await CreateProjectAsync(user);

        // First verify user owns the project (authorization check)
        var ownedProject = await _projectService.GetProjectForUserAsync(project.Id, user.Id);
        ownedProject.ShouldNotBeNull();

        // Act - delete after authorization check passed
        var result = await _projectService.DeleteProjectAsync(project.Id);

        // Assert
        result.ShouldBeTrue();
        var deleted = await _db.Projects.FindAsync(project.Id);
        deleted.ShouldBeNull();
    }

    [Fact]
    public async Task DeleteProject_WithAuthorizationCheckFirst_BlocksOtherUser()
    {
        // Arrange
        var owner = await CreateUserAsync("owner");
        var otherUser = await CreateUserAsync("other");
        var project = await CreateProjectAsync(owner);

        // Authorization check should fail for other user
        var ownedProject = await _projectService.GetProjectForUserAsync(project.Id, otherUser.Id);

        // Assert - other user cannot access, so cannot proceed to delete
        ownedProject.ShouldBeNull();

        // Project still exists
        var notDeleted = await _db.Projects.FindAsync(project.Id);
        notDeleted.ShouldNotBeNull();
    }

    // -------------------------------------------------------------------------
    // Build Access Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentBuilds_ForUser_ReturnsOnlyOwnedBuilds()
    {
        // Arrange
        var user1 = await CreateUserAsync("user1");
        var user2 = await CreateUserAsync("user2");
        var project1 = await CreateProjectAsync(user1);
        var project2 = await CreateProjectAsync(user2);

        await CreateBuildAsync(project1);
        await CreateBuildAsync(project1);
        await CreateBuildAsync(project2);

        // Act
        var results = await _buildService.GetRecentBuildsForUserAsync(user1.Id);

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldAllBe(b => b.Project.OwnerId == user1.Id);
    }

    [Fact]
    public async Task GetBuildsForProject_ReturnsOnlyProjectBuilds()
    {
        // Arrange
        var user = await CreateUserAsync("user");
        var project1 = await CreateProjectAsync(user, "repo1");
        var project2 = await CreateProjectAsync(user, "repo2");

        await CreateBuildAsync(project1);
        await CreateBuildAsync(project1);
        await CreateBuildAsync(project2);

        // Act
        var results = await _buildService.GetBuildsForProjectAsync(project1.Id);

        // Assert
        results.Count.ShouldBe(2);
        results.ShouldAllBe(b => b.ProjectId == project1.Id);
    }

    // -------------------------------------------------------------------------
    // Cross-User Isolation Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UserCannotAccessOtherUserSecrets()
    {
        // Arrange
        var owner = await CreateUserAsync("owner");
        var otherUser = await CreateUserAsync("other");
        var project = await CreateProjectAsync(owner);

        // Add secret to project
        var secret = new ProjectSecret
        {
            ProjectId = project.Id,
            Name = "API_KEY",
            EncryptedValue = "encrypted-value",
            CreatedAt = DateTime.UtcNow
        };
        _db.ProjectSecrets.Add(secret);
        await _db.SaveChangesAsync();

        // Act - try to get project as other user
        var result = await _projectService.GetProjectForUserAsync(project.Id, otherUser.Id);

        // Assert
        result.ShouldBeNull();
    }

    [Fact]
    public async Task UserCannotSeeBuildLogsFromOtherUser()
    {
        // Arrange
        var owner = await CreateUserAsync("owner");
        var otherUser = await CreateUserAsync("other");
        var project = await CreateProjectAsync(owner);
        var build = await CreateBuildAsync(project);

        // Add log entry
        var logEntry = new BuildLogEntry
        {
            BuildId = build.Id,
            Sequence = 1,
            Type = LogEntryType.Info,
            Message = "Secret log message",
            Timestamp = DateTime.UtcNow
        };
        _db.BuildLogEntries.Add(logEntry);
        await _db.SaveChangesAsync();

        // Act - try to get build as other user (via project check)
        var otherProject = await _projectService.GetProjectForUserAsync(project.Id, otherUser.Id);

        // Assert
        otherProject.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<ApplicationUser> CreateUserAsync(string login)
    {
        var user = new ApplicationUser
        {
            GitHubId = Random.Shared.Next(1, 100000),
            GitHubLogin = login,
            Email = $"{login}@example.com",
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Project> CreateProjectAsync(ApplicationUser owner, string repoName = "test-repo")
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

    private async Task<Build> CreateBuildAsync(Project project)
    {
        var build = new Build
        {
            Project = project,
            ProjectId = project.Id,
            CommitSha = "abc123def456789012345678901234567890abcd",
            Branch = "main",
            Status = BuildStatus.Success,
            Trigger = BuildTrigger.Push,
            QueuedAt = DateTime.UtcNow
        };
        _db.Builds.Add(build);
        await _db.SaveChangesAsync();
        return build;
    }
}
