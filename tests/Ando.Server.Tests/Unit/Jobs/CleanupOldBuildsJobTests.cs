// =============================================================================
// CleanupOldBuildsJobTests.cs
//
// Summary: Unit tests for the CleanupOldBuildsJob.
//
// Tests orphaned build detection and status updates for builds stuck in
// Running or Queued states beyond their maximum allowed duration.
// =============================================================================

using Ando.Server.Jobs;
using Ando.Server.Models;
using Ando.Server.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ando.Server.Tests.Unit.Jobs;

public class CleanupOldBuildsJobTests : IDisposable
{
    private readonly Data.AndoDbContext _db;
    private readonly CleanupOldBuildsJob _job;
    private readonly User _testUser;
    private readonly Project _testProject;

    // Match the job's internal constants
    private static readonly TimeSpan MaxRunningDuration = TimeSpan.FromHours(2);
    private static readonly TimeSpan MaxQueuedDuration = TimeSpan.FromHours(24);

    public CleanupOldBuildsJobTests()
    {
        _db = TestDbContextFactory.Create();
        _job = new CleanupOldBuildsJob(_db, NullLogger<CleanupOldBuildsJob>.Instance);

        // Create test data
        _testUser = new User
        {
            GitHubId = 12345,
            GitHubLogin = "testuser",
            CreatedAt = DateTime.UtcNow
        };
        _db.Users.Add(_testUser);

        _testProject = new Project
        {
            Owner = _testUser,
            GitHubRepoId = 99999,
            RepoFullName = "testuser/test-repo",
            RepoUrl = "https://github.com/testuser/test-repo",
            DefaultBranch = "main",
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(_testProject);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
    }

    // -------------------------------------------------------------------------
    // Orphaned Running Builds Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithOrphanedRunningBuild_MarksAsTimedOut()
    {
        // Arrange - build running for more than 2 hours
        var build = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: DateTime.UtcNow - MaxRunningDuration - TimeSpan.FromMinutes(10));

        // Act
        await _job.ExecuteAsync();

        // Assert
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(BuildStatus.TimedOut);
        updated.FinishedAt.ShouldNotBeNull();
        updated.ErrorMessage.ShouldContain("exceeded maximum running time");
    }

    [Fact]
    public async Task ExecuteAsync_WithRecentRunningBuild_DoesNotModify()
    {
        // Arrange - build running for less than 2 hours
        var build = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: DateTime.UtcNow - TimeSpan.FromHours(1));

        // Act
        await _job.ExecuteAsync();

        // Assert
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(BuildStatus.Running);
        updated.FinishedAt.ShouldBeNull();
        updated.ErrorMessage.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithRunningBuildJustUnderCutoff_DoesNotModify()
    {
        // Arrange - build running for slightly less than 2 hours (not past cutoff)
        // Add 1 second buffer to account for test execution timing
        var build = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: DateTime.UtcNow - MaxRunningDuration + TimeSpan.FromSeconds(1));

        // Act
        await _job.ExecuteAsync();

        // Assert
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(BuildStatus.Running);
    }

    [Fact]
    public async Task ExecuteAsync_OrphanedRunningBuild_SetsDuration()
    {
        // Arrange
        var startedAt = DateTime.UtcNow - MaxRunningDuration - TimeSpan.FromHours(1);
        var build = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: startedAt);

        // Act
        await _job.ExecuteAsync();

        // Assert
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Duration.ShouldNotBeNull();
        updated.Duration!.Value.TotalHours.ShouldBeGreaterThan(3);
    }

    // -------------------------------------------------------------------------
    // Orphaned Queued Builds Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithOrphanedQueuedBuild_MarksAsFailed()
    {
        // Arrange - build queued for more than 24 hours
        var build = await CreateBuildAsync(
            status: BuildStatus.Queued,
            queuedAt: DateTime.UtcNow - MaxQueuedDuration - TimeSpan.FromHours(1));

        // Act
        await _job.ExecuteAsync();

        // Assert
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(BuildStatus.Failed);
        updated.FinishedAt.ShouldNotBeNull();
        updated.ErrorMessage.ShouldContain("exceeded maximum queue time");
    }

    [Fact]
    public async Task ExecuteAsync_WithRecentQueuedBuild_DoesNotModify()
    {
        // Arrange - build queued for less than 24 hours
        var build = await CreateBuildAsync(
            status: BuildStatus.Queued,
            queuedAt: DateTime.UtcNow - TimeSpan.FromHours(12));

        // Act
        await _job.ExecuteAsync();

        // Assert
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(BuildStatus.Queued);
        updated.FinishedAt.ShouldBeNull();
    }

    [Fact]
    public async Task ExecuteAsync_WithQueuedBuildJustUnderCutoff_DoesNotModify()
    {
        // Arrange - build queued for slightly less than 24 hours
        // Add 1 second buffer to account for test execution timing
        var build = await CreateBuildAsync(
            status: BuildStatus.Queued,
            queuedAt: DateTime.UtcNow - MaxQueuedDuration + TimeSpan.FromSeconds(1));

        // Act
        await _job.ExecuteAsync();

        // Assert
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(BuildStatus.Queued);
    }

    // -------------------------------------------------------------------------
    // Completed Builds Tests
    // -------------------------------------------------------------------------

    [Theory]
    [InlineData(BuildStatus.Success)]
    [InlineData(BuildStatus.Failed)]
    [InlineData(BuildStatus.Cancelled)]
    [InlineData(BuildStatus.TimedOut)]
    public async Task ExecuteAsync_WithCompletedBuild_DoesNotModify(BuildStatus status)
    {
        // Arrange - completed build from long ago
        var build = await CreateBuildAsync(
            status: status,
            queuedAt: DateTime.UtcNow - TimeSpan.FromDays(30));
        build.FinishedAt = DateTime.UtcNow - TimeSpan.FromDays(30);
        await _db.SaveChangesAsync();

        // Act
        await _job.ExecuteAsync();

        // Assert - status unchanged
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(status);
    }

    // -------------------------------------------------------------------------
    // Mixed Builds Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithMixedBuilds_UpdatesOnlyOrphaned()
    {
        // Arrange
        var orphanedRunning = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: DateTime.UtcNow - TimeSpan.FromHours(3));

        var recentRunning = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: DateTime.UtcNow - TimeSpan.FromMinutes(30));

        var orphanedQueued = await CreateBuildAsync(
            status: BuildStatus.Queued,
            queuedAt: DateTime.UtcNow - TimeSpan.FromHours(25));

        var recentQueued = await CreateBuildAsync(
            status: BuildStatus.Queued,
            queuedAt: DateTime.UtcNow - TimeSpan.FromHours(1));

        var completed = await CreateBuildAsync(
            status: BuildStatus.Success,
            queuedAt: DateTime.UtcNow - TimeSpan.FromDays(7));

        // Act
        await _job.ExecuteAsync();

        // Assert
        (await _db.Builds.FindAsync(orphanedRunning.Id))!.Status.ShouldBe(BuildStatus.TimedOut);
        (await _db.Builds.FindAsync(recentRunning.Id))!.Status.ShouldBe(BuildStatus.Running);
        (await _db.Builds.FindAsync(orphanedQueued.Id))!.Status.ShouldBe(BuildStatus.Failed);
        (await _db.Builds.FindAsync(recentQueued.Id))!.Status.ShouldBe(BuildStatus.Queued);
        (await _db.Builds.FindAsync(completed.Id))!.Status.ShouldBe(BuildStatus.Success);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleOrphanedBuilds_UpdatesAll()
    {
        // Arrange
        var builds = new List<Build>();
        for (int i = 0; i < 5; i++)
        {
            builds.Add(await CreateBuildAsync(
                status: BuildStatus.Running,
                startedAt: DateTime.UtcNow - TimeSpan.FromHours(3 + i)));
        }

        // Act
        await _job.ExecuteAsync();

        // Assert
        foreach (var build in builds)
        {
            var updated = await _db.Builds.FindAsync(build.Id);
            updated!.Status.ShouldBe(BuildStatus.TimedOut);
        }
    }

    // -------------------------------------------------------------------------
    // No-Op Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithNoBuilds_CompletesSuccessfully()
    {
        // Arrange - no builds

        // Act
        await _job.ExecuteAsync();

        // Assert - should complete without error
        var count = await _db.Builds.CountAsync();
        count.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithOnlyCompletedBuilds_DoesNothing()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            var build = await CreateBuildAsync(
                status: BuildStatus.Success,
                queuedAt: DateTime.UtcNow - TimeSpan.FromDays(i + 1));
            build.FinishedAt = DateTime.UtcNow - TimeSpan.FromDays(i + 1);
        }
        await _db.SaveChangesAsync();

        // Act
        await _job.ExecuteAsync();

        // Assert - all still success
        var builds = await _db.Builds.ToListAsync();
        builds.ShouldAllBe(b => b.Status == BuildStatus.Success);
    }

    // -------------------------------------------------------------------------
    // Edge Cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithRunningBuildNoStartedAt_DoesNotCrash()
    {
        // Arrange - running build without StartedAt (edge case)
        var build = new Build
        {
            Project = _testProject,
            ProjectId = _testProject.Id,
            CommitSha = "abc123",
            Branch = "main",
            Status = BuildStatus.Running,
            Trigger = BuildTrigger.Push,
            QueuedAt = DateTime.UtcNow - TimeSpan.FromHours(3),
            StartedAt = null // No StartedAt
        };
        _db.Builds.Add(build);
        await _db.SaveChangesAsync();

        // Act
        await _job.ExecuteAsync();

        // Assert - build not modified (StartedAt is null so doesn't match query)
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(BuildStatus.Running);
    }

    [Fact]
    public async Task ExecuteAsync_PreservesExistingBuildData()
    {
        // Arrange
        var build = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: DateTime.UtcNow - TimeSpan.FromHours(3));
        build.CommitMessage = "Test commit";
        build.CommitAuthor = "Test Author";
        build.StepsTotal = 5;
        build.StepsCompleted = 3;
        build.StepsFailed = 1;
        await _db.SaveChangesAsync();

        // Act
        await _job.ExecuteAsync();

        // Assert - existing data preserved
        var updated = await _db.Builds.FindAsync(build.Id);
        updated!.Status.ShouldBe(BuildStatus.TimedOut);
        updated.CommitMessage.ShouldBe("Test commit");
        updated.CommitAuthor.ShouldBe("Test Author");
        updated.StepsTotal.ShouldBe(5);
        updated.StepsCompleted.ShouldBe(3);
        updated.StepsFailed.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_BuildsFromDifferentProjects_HandledCorrectly()
    {
        // Arrange
        var project2 = new Project
        {
            Owner = _testUser,
            OwnerId = _testUser.Id,
            GitHubRepoId = 88888,
            RepoFullName = "testuser/other-repo",
            RepoUrl = "https://github.com/testuser/other-repo",
            DefaultBranch = "main",
            CreatedAt = DateTime.UtcNow
        };
        _db.Projects.Add(project2);
        await _db.SaveChangesAsync();

        var build1 = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: DateTime.UtcNow - TimeSpan.FromHours(3),
            project: _testProject);

        var build2 = await CreateBuildAsync(
            status: BuildStatus.Running,
            startedAt: DateTime.UtcNow - TimeSpan.FromHours(3),
            project: project2);

        // Act
        await _job.ExecuteAsync();

        // Assert - both marked as timed out
        (await _db.Builds.FindAsync(build1.Id))!.Status.ShouldBe(BuildStatus.TimedOut);
        (await _db.Builds.FindAsync(build2.Id))!.Status.ShouldBe(BuildStatus.TimedOut);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<Build> CreateBuildAsync(
        BuildStatus status,
        DateTime? queuedAt = null,
        DateTime? startedAt = null,
        Project? project = null)
    {
        project ??= _testProject;

        var build = new Build
        {
            Project = project,
            ProjectId = project.Id,
            CommitSha = Guid.NewGuid().ToString("N")[..8],
            Branch = "main",
            Status = status,
            Trigger = BuildTrigger.Push,
            QueuedAt = queuedAt ?? DateTime.UtcNow,
            StartedAt = startedAt
        };

        _db.Builds.Add(build);
        await _db.SaveChangesAsync();
        return build;
    }
}
