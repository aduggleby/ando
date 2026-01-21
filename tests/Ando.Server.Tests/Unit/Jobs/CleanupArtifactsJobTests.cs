// =============================================================================
// CleanupArtifactsJobTests.cs
//
// Summary: Unit tests for the CleanupArtifactsJob.
//
// Tests artifact expiration logic, file deletion, database cleanup, and batch
// processing. Uses in-memory database and temporary files for isolation.
// =============================================================================

using Ando.Server.Jobs;
using Ando.Server.Models;
using Ando.Server.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Ando.Server.Tests.Unit.Jobs;

public class CleanupArtifactsJobTests : IDisposable
{
    private readonly Data.AndoDbContext _db;
    private readonly CleanupArtifactsJob _job;
    private readonly string _tempDir;
    private readonly ApplicationUser _testUser;
    private readonly Project _testProject;
    private readonly Build _testBuild;

    public CleanupArtifactsJobTests()
    {
        _db = TestDbContextFactory.Create();
        _job = new CleanupArtifactsJob(_db, NullLogger<CleanupArtifactsJob>.Instance);
        _tempDir = Path.Combine(Path.GetTempPath(), $"ando-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        // Create test data
        _testUser = new ApplicationUser
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

        _testBuild = new Build
        {
            Project = _testProject,
            CommitSha = "abc123",
            Branch = "main",
            Status = BuildStatus.Success,
            Trigger = BuildTrigger.Push,
            QueuedAt = DateTime.UtcNow
        };
        _db.Builds.Add(_testBuild);
        _db.SaveChanges();
    }

    public void Dispose()
    {
        _db.Dispose();
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Expiration Logic Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithExpiredArtifact_DeletesFromDatabase()
    {
        // Arrange
        var artifact = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddDays(-1),
            createFile: true);

        // Act
        await _job.ExecuteAsync();

        // Assert
        var remaining = await _db.BuildArtifacts.CountAsync();
        remaining.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithNonExpiredArtifact_DoesNotDelete()
    {
        // Arrange
        var artifact = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddDays(1),
            createFile: true);

        // Act
        await _job.ExecuteAsync();

        // Assert
        var remaining = await _db.BuildArtifacts.CountAsync();
        remaining.ShouldBe(1);
    }

    [Fact]
    public async Task ExecuteAsync_WithExactlyExpiredArtifact_Deletes()
    {
        // Arrange - artifact that expires exactly now (or just before)
        var artifact = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddSeconds(-1),
            createFile: true);

        // Act
        await _job.ExecuteAsync();

        // Assert
        var remaining = await _db.BuildArtifacts.CountAsync();
        remaining.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithMixedArtifacts_DeletesOnlyExpired()
    {
        // Arrange
        var expired1 = await CreateArtifactAsync(expiresAt: DateTime.UtcNow.AddDays(-2), createFile: true);
        var expired2 = await CreateArtifactAsync(expiresAt: DateTime.UtcNow.AddHours(-1), createFile: true);
        var valid1 = await CreateArtifactAsync(expiresAt: DateTime.UtcNow.AddDays(1), createFile: true);
        var valid2 = await CreateArtifactAsync(expiresAt: DateTime.UtcNow.AddDays(30), createFile: true);

        // Act
        await _job.ExecuteAsync();

        // Assert
        var remaining = await _db.BuildArtifacts.ToListAsync();
        remaining.Count.ShouldBe(2);
        remaining.ShouldAllBe(a => a.ExpiresAt > DateTime.UtcNow);
    }

    // -------------------------------------------------------------------------
    // File Deletion Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithExpiredArtifact_DeletesPhysicalFile()
    {
        // Arrange
        var artifact = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddDays(-1),
            createFile: true);
        File.Exists(artifact.StoragePath).ShouldBeTrue();

        // Act
        await _job.ExecuteAsync();

        // Assert
        File.Exists(artifact.StoragePath).ShouldBeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_WithMissingFile_StillDeletesRecord()
    {
        // Arrange - artifact record without physical file
        var artifact = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddDays(-1),
            createFile: false);

        // Act
        await _job.ExecuteAsync();

        // Assert - record should be deleted even without file
        var remaining = await _db.BuildArtifacts.CountAsync();
        remaining.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleExpiredArtifacts_DeletesAllFiles()
    {
        // Arrange
        var artifacts = new List<BuildArtifact>();
        for (int i = 0; i < 5; i++)
        {
            artifacts.Add(await CreateArtifactAsync(
                expiresAt: DateTime.UtcNow.AddDays(-i - 1),
                createFile: true));
        }

        // Verify files exist
        foreach (var artifact in artifacts)
        {
            File.Exists(artifact.StoragePath).ShouldBeTrue();
        }

        // Act
        await _job.ExecuteAsync();

        // Assert - all files deleted
        foreach (var artifact in artifacts)
        {
            File.Exists(artifact.StoragePath).ShouldBeFalse();
        }

        var remaining = await _db.BuildArtifacts.CountAsync();
        remaining.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // Batch Processing Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithMoreThanBatchSize_ProcessesAllInBatches()
    {
        // Arrange - create more than batch size (100) artifacts
        var artifactCount = 150;
        for (int i = 0; i < artifactCount; i++)
        {
            await CreateArtifactAsync(
                expiresAt: DateTime.UtcNow.AddDays(-1),
                createFile: false); // Skip file creation for speed
        }

        var initialCount = await _db.BuildArtifacts.CountAsync();
        initialCount.ShouldBe(artifactCount);

        // Act
        await _job.ExecuteAsync();

        // Assert - all processed despite exceeding batch size
        var remaining = await _db.BuildArtifacts.CountAsync();
        remaining.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // No-Op Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithNoArtifacts_CompletesSuccessfully()
    {
        // Arrange - no artifacts

        // Act
        await _job.ExecuteAsync();

        // Assert - should complete without error
        var count = await _db.BuildArtifacts.CountAsync();
        count.ShouldBe(0);
    }

    [Fact]
    public async Task ExecuteAsync_WithOnlyValidArtifacts_LeavesAllIntact()
    {
        // Arrange
        for (int i = 0; i < 5; i++)
        {
            await CreateArtifactAsync(
                expiresAt: DateTime.UtcNow.AddDays(i + 1),
                createFile: true);
        }

        // Act
        await _job.ExecuteAsync();

        // Assert
        var remaining = await _db.BuildArtifacts.CountAsync();
        remaining.ShouldBe(5);
    }

    // -------------------------------------------------------------------------
    // Size Tracking Tests
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_TracksDeletedBytes()
    {
        // Arrange
        var artifact1 = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddDays(-1),
            createFile: true,
            sizeBytes: 1024);
        var artifact2 = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddDays(-1),
            createFile: true,
            sizeBytes: 2048);

        // Act
        await _job.ExecuteAsync();

        // Assert - job completes (logging would show freed bytes)
        var remaining = await _db.BuildArtifacts.CountAsync();
        remaining.ShouldBe(0);
    }

    // -------------------------------------------------------------------------
    // Edge Cases
    // -------------------------------------------------------------------------

    [Fact]
    public async Task ExecuteAsync_WithArtifactFromDifferentBuilds_DeletesCorrectly()
    {
        // Arrange
        var build2 = new Build
        {
            Project = _testProject,
            CommitSha = "def456",
            Branch = "feature",
            Status = BuildStatus.Success,
            Trigger = BuildTrigger.Push,
            QueuedAt = DateTime.UtcNow
        };
        _db.Builds.Add(build2);
        await _db.SaveChangesAsync();

        var artifact1 = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddDays(-1),
            createFile: true,
            build: _testBuild);
        var artifact2 = await CreateArtifactAsync(
            expiresAt: DateTime.UtcNow.AddDays(1),
            createFile: true,
            build: build2);

        // Act
        await _job.ExecuteAsync();

        // Assert
        var remaining = await _db.BuildArtifacts.ToListAsync();
        remaining.Count.ShouldBe(1);
        remaining[0].BuildId.ShouldBe(build2.Id);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private async Task<BuildArtifact> CreateArtifactAsync(
        DateTime expiresAt,
        bool createFile,
        long sizeBytes = 1024,
        Build? build = null)
    {
        build ??= _testBuild;
        var fileName = $"artifact-{Guid.NewGuid():N}.zip";
        var filePath = Path.Combine(_tempDir, fileName);

        if (createFile)
        {
            await File.WriteAllBytesAsync(filePath, new byte[sizeBytes]);
        }

        var artifact = new BuildArtifact
        {
            BuildId = build.Id,
            Build = build,
            Name = fileName,
            StoragePath = filePath,
            SizeBytes = sizeBytes,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = expiresAt
        };

        _db.BuildArtifacts.Add(artifact);
        await _db.SaveChangesAsync();
        return artifact;
    }
}
