// =============================================================================
// BuildServiceTests.cs
//
// Summary: Unit tests for the BuildService.
//
// Tests build queuing, cancellation, retry, and status updates.
// Uses in-memory database for isolation.
// =============================================================================

using Ando.Server.BuildExecution;
using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Hangfire;
using Hangfire.States;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Ando.Server.Tests.Unit.Services;

public class BuildServiceTests : IDisposable
{
    private readonly AndoDbContext _db;
    private readonly CancellationTokenRegistry _cancellationRegistry;
    private readonly Mock<IBackgroundJobClient> _jobClient;
    private readonly BuildService _service;

    public BuildServiceTests()
    {
        _db = TestDbContextFactory.Create();
        _cancellationRegistry = new CancellationTokenRegistry();
        _jobClient = new Mock<IBackgroundJobClient>();

        _service = new BuildService(
            _db,
            _jobClient.Object,
            _cancellationRegistry,
            NullLogger<BuildService>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // -------------------------------------------------------------------------
    // QueueBuildAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task QueueBuildAsync_CreatesBuildWithQueuedStatus()
    {
        // Arrange
        var project = await CreateTestProjectAsync();

        // Act
        var buildId = await _service.QueueBuildAsync(
            project.Id,
            "abc123def456789012345678901234567890abcd",
            "main",
            BuildTrigger.Push);

        // Assert
        var build = await _db.Builds.FindAsync(buildId);
        build.ShouldNotBeNull();
        build.Status.ShouldBe(BuildStatus.Queued);
        build.ProjectId.ShouldBe(project.Id);
    }

    [Fact]
    public async Task QueueBuildAsync_SetsCommitDetails()
    {
        // Arrange
        var project = await CreateTestProjectAsync();

        // Act
        var buildId = await _service.QueueBuildAsync(
            project.Id,
            "abc123def456789012345678901234567890abcd",
            "feature/test",
            BuildTrigger.Push,
            commitMessage: "Fix bug",
            commitAuthor: "John Doe");

        // Assert
        var build = await _db.Builds.FindAsync(buildId);
        build.ShouldNotBeNull();
        build.CommitSha.ShouldBe("abc123def456789012345678901234567890abcd");
        build.Branch.ShouldBe("feature/test");
        build.CommitMessage.ShouldBe("Fix bug");
        build.CommitAuthor.ShouldBe("John Doe");
    }

    [Fact]
    public async Task QueueBuildAsync_SetsPullRequestNumber()
    {
        // Arrange
        var project = await CreateTestProjectAsync();

        // Act
        var buildId = await _service.QueueBuildAsync(
            project.Id,
            "abc123def456789012345678901234567890abcd",
            "feature/pr-branch",
            BuildTrigger.PullRequest,
            pullRequestNumber: 42);

        // Assert
        var build = await _db.Builds.FindAsync(buildId);
        build.ShouldNotBeNull();
        build.Trigger.ShouldBe(BuildTrigger.PullRequest);
        build.PullRequestNumber.ShouldBe(42);
    }

    [Fact]
    public async Task QueueBuildAsync_SetsQueuedAt()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var beforeQueue = DateTime.UtcNow;

        // Act
        var buildId = await _service.QueueBuildAsync(
            project.Id,
            "abc123def456789012345678901234567890abcd",
            "main",
            BuildTrigger.Push);

        // Assert
        var build = await _db.Builds.FindAsync(buildId);
        build.ShouldNotBeNull();
        build.QueuedAt.ShouldBeGreaterThanOrEqualTo(beforeQueue);
        build.QueuedAt.ShouldBeLessThanOrEqualTo(DateTime.UtcNow);
    }

    [Fact]
    public async Task QueueBuildAsync_EnqueuesHangfireJob()
    {
        // Arrange
        var project = await CreateTestProjectAsync();

        // Act
        await _service.QueueBuildAsync(
            project.Id,
            "abc123def456789012345678901234567890abcd",
            "main",
            BuildTrigger.Push);

        // Assert
        _jobClient.Verify(
            c => c.Create(
                It.IsAny<Hangfire.Common.Job>(),
                It.IsAny<IState>()),
            Times.Once);
    }

    // -------------------------------------------------------------------------
    // CancelBuildAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task CancelBuildAsync_WithRegisteredBuild_ReturnsTrue()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Running);

        // Register the build as running (required for cancellation to work)
        using var cts = new CancellationTokenSource();
        _cancellationRegistry.Register(build.Id, cts);

        // Act
        var result = await _service.CancelBuildAsync(build.Id);

        // Assert - cancellation is triggered, but status update happens in orchestrator
        result.ShouldBeTrue();
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task CancelBuildAsync_CallsCancellationRegistry()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Running);

        // Register the build as running
        using var cts = new CancellationTokenSource();
        _cancellationRegistry.Register(build.Id, cts);

        // Act
        await _service.CancelBuildAsync(build.Id);

        // Assert
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task CancelBuildAsync_WithQueuedBuild_CancelsAndUpdatesStatus()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Queued);
        build.HangfireJobId = "test-job-123";
        await _db.SaveChangesAsync();

        // Act
        var result = await _service.CancelBuildAsync(build.Id);

        // Assert
        result.ShouldBeTrue();
        var updatedBuild = await _db.Builds.FindAsync(build.Id);
        updatedBuild!.Status.ShouldBe(BuildStatus.Cancelled);
        updatedBuild.FinishedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task CancelBuildAsync_WithQueuedBuildNoJobId_ReturnsFalse()
    {
        // Arrange - queued build without HangfireJobId (edge case)
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Queued);
        // HangfireJobId is null

        // Act
        var result = await _service.CancelBuildAsync(build.Id);

        // Assert - cannot cancel without job ID
        result.ShouldBeFalse();
        var updatedBuild = await _db.Builds.FindAsync(build.Id);
        updatedBuild!.Status.ShouldBe(BuildStatus.Queued);
    }

    [Fact]
    public async Task CancelBuildAsync_WithUnregisteredRunningBuild_ReturnsFalse()
    {
        // Arrange - running build that isn't registered in cancellation registry
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Running);
        // Intentionally not registering in _cancellationRegistry

        // Act
        var result = await _service.CancelBuildAsync(build.Id);

        // Assert - cannot cancel because not in registry
        result.ShouldBeFalse();
    }

    [Fact]
    public async Task CancelBuildAsync_WithCompletedBuild_ReturnsFalse()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Success);

        // Act
        var result = await _service.CancelBuildAsync(build.Id);

        // Assert
        result.ShouldBeFalse();
        var updatedBuild = await _db.Builds.FindAsync(build.Id);
        updatedBuild!.Status.ShouldBe(BuildStatus.Success);
    }

    [Fact]
    public async Task CancelBuildAsync_WithNonExistentBuild_ReturnsFalse()
    {
        // Act
        var result = await _service.CancelBuildAsync(99999);

        // Assert
        result.ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // RetryBuildAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task RetryBuildAsync_CreatesNewBuildWithSameSettings()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var originalBuild = await CreateTestBuildAsync(project, BuildStatus.Failed);
        originalBuild.CommitSha = "abc123def456789012345678901234567890abcd";
        originalBuild.Branch = "main";
        originalBuild.CommitMessage = "Original commit";
        originalBuild.CommitAuthor = "John Doe";
        await _db.SaveChangesAsync();

        // Act
        var newBuildId = await _service.RetryBuildAsync(originalBuild.Id);

        // Assert
        var newBuild = await _db.Builds.FindAsync(newBuildId);
        newBuild.ShouldNotBeNull();
        newBuild.Id.ShouldNotBe(originalBuild.Id);
        newBuild.ProjectId.ShouldBe(originalBuild.ProjectId);
        newBuild.CommitSha.ShouldBe(originalBuild.CommitSha);
        newBuild.Branch.ShouldBe(originalBuild.Branch);
        newBuild.CommitMessage.ShouldBe(originalBuild.CommitMessage);
        newBuild.CommitAuthor.ShouldBe(originalBuild.CommitAuthor);
        newBuild.Status.ShouldBe(BuildStatus.Queued);
    }

    [Fact]
    public async Task RetryBuildAsync_SetsTriggerToManual()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var originalBuild = await CreateTestBuildAsync(project, BuildStatus.Failed);
        originalBuild.Trigger = BuildTrigger.Push;
        await _db.SaveChangesAsync();

        // Act
        var newBuildId = await _service.RetryBuildAsync(originalBuild.Id);

        // Assert
        var newBuild = await _db.Builds.FindAsync(newBuildId);
        newBuild!.Trigger.ShouldBe(BuildTrigger.Manual);
    }

    // -------------------------------------------------------------------------
    // GetBuildAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBuildAsync_ReturnsBuildWithProject()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Success);

        // Act
        var result = await _service.GetBuildAsync(build.Id);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe(build.Id);
        result.Project.ShouldNotBeNull();
        result.Project.Id.ShouldBe(project.Id);
    }

    [Fact]
    public async Task GetBuildAsync_WithNonExistentId_ReturnsNull()
    {
        // Act
        var result = await _service.GetBuildAsync(99999);

        // Assert
        result.ShouldBeNull();
    }

    // -------------------------------------------------------------------------
    // GetBuildsForProjectAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetBuildsForProjectAsync_ReturnsBuildsInDescendingOrder()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build1 = await CreateTestBuildAsync(project, BuildStatus.Success);
        var build2 = await CreateTestBuildAsync(project, BuildStatus.Failed);
        var build3 = await CreateTestBuildAsync(project, BuildStatus.Running);

        // Act
        var results = await _service.GetBuildsForProjectAsync(project.Id);

        // Assert
        results.Count.ShouldBe(3);
        results[0].Id.ShouldBe(build3.Id); // Most recent first
    }

    [Fact]
    public async Task GetBuildsForProjectAsync_RespectsPagination()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        for (int i = 0; i < 5; i++)
        {
            await CreateTestBuildAsync(project, BuildStatus.Success);
        }

        // Act
        var results = await _service.GetBuildsForProjectAsync(project.Id, skip: 1, take: 2);

        // Assert
        results.Count.ShouldBe(2);
    }

    // -------------------------------------------------------------------------
    // GetRecentBuildsForUserAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task GetRecentBuildsForUserAsync_ReturnsOnlyUserBuilds()
    {
        // Arrange
        var user1 = await CreateTestUserAsync("user1");
        var user2 = await CreateTestUserAsync("user2");
        var project1 = await CreateTestProjectAsync(user1);
        var project2 = await CreateTestProjectAsync(user2);

        await CreateTestBuildAsync(project1, BuildStatus.Success);
        await CreateTestBuildAsync(project2, BuildStatus.Success);

        // Act
        var results = await _service.GetRecentBuildsForUserAsync(user1.Id);

        // Assert
        results.Count.ShouldBe(1);
        results[0].Project.OwnerId.ShouldBe(user1.Id);
    }

    // -------------------------------------------------------------------------
    // UpdateBuildStatusAsync Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateBuildStatusAsync_UpdatesStatus()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Running);

        // Act
        await _service.UpdateBuildStatusAsync(build.Id, BuildStatus.Success);

        // Assert
        var updatedBuild = await _db.Builds.FindAsync(build.Id);
        updatedBuild!.Status.ShouldBe(BuildStatus.Success);
    }

    [Fact]
    public async Task UpdateBuildStatusAsync_SetsErrorMessage()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Running);

        // Act
        await _service.UpdateBuildStatusAsync(
            build.Id,
            BuildStatus.Failed,
            errorMessage: "Build script error");

        // Assert
        var updatedBuild = await _db.Builds.FindAsync(build.Id);
        updatedBuild!.Status.ShouldBe(BuildStatus.Failed);
        updatedBuild.ErrorMessage.ShouldBe("Build script error");
    }

    [Fact]
    public async Task UpdateBuildStatusAsync_UpdatesStepCounts()
    {
        // Arrange
        var project = await CreateTestProjectAsync();
        var build = await CreateTestBuildAsync(project, BuildStatus.Running);

        // Act
        await _service.UpdateBuildStatusAsync(
            build.Id,
            BuildStatus.Success,
            stepsTotal: 5,
            stepsCompleted: 4,
            stepsFailed: 1);

        // Assert
        var updatedBuild = await _db.Builds.FindAsync(build.Id);
        updatedBuild!.StepsTotal.ShouldBe(5);
        updatedBuild.StepsCompleted.ShouldBe(4);
        updatedBuild.StepsFailed.ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<User> CreateTestUserAsync(string login = "testuser")
    {
        var user = new User
        {
            GitHubId = Random.Shared.Next(1, 100000),
            GitHubLogin = login,
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    private async Task<Project> CreateTestProjectAsync(User? owner = null)
    {
        owner ??= await CreateTestUserAsync();

        var project = new Project
        {
            Owner = owner,
            GitHubRepoId = Random.Shared.Next(1, 100000),
            RepoFullName = $"{owner.GitHubLogin}/test-repo",
            RepoUrl = $"https://github.com/{owner.GitHubLogin}/test-repo",
            DefaultBranch = "main",
            BranchFilter = "main",
            InstallationId = 111,
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
}
