// =============================================================================
// BuildOrchestratorTests.cs
//
// Summary: Unit tests for build execution components.
//
// Tests the CancellationTokenRegistry used for build cancellation.
// The full BuildOrchestrator is better tested via integration tests
// as it requires complex service provider setup.
// =============================================================================

using Ando.Server.BuildExecution;

namespace Ando.Server.Tests.Unit;

/// <summary>
/// Tests for the CancellationTokenRegistry.
/// </summary>
public class CancellationTokenRegistryTests
{
    [Fact]
    public void Register_AddsBuildToRegistry()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();
        using var cts = new CancellationTokenSource();

        // Act
        registry.Register(1, cts);

        // Assert
        registry.IsRunning(1).ShouldBeTrue();
    }

    [Fact]
    public void Unregister_RemovesBuildFromRegistry()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();
        using var cts = new CancellationTokenSource();
        registry.Register(1, cts);

        // Act
        registry.Unregister(1);

        // Assert
        registry.IsRunning(1).ShouldBeFalse();
    }

    [Fact]
    public void IsRunning_ReturnsFalseForUnregisteredBuild()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();

        // Act & Assert
        registry.IsRunning(999).ShouldBeFalse();
    }

    [Fact]
    public void TryCancel_CancelsBuildAndReturnsTrue()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();
        using var cts = new CancellationTokenSource();
        registry.Register(1, cts);

        // Act
        var result = registry.TryCancel(1);

        // Assert
        result.ShouldBeTrue();
        cts.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void TryCancel_ReturnsFalseForUnregisteredBuild()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();

        // Act
        var result = registry.TryCancel(999);

        // Assert
        result.ShouldBeFalse();
    }

    [Fact]
    public void Register_OverwritesPreviousRegistration()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        registry.Register(1, cts1);

        // Act
        registry.Register(1, cts2);
        registry.TryCancel(1);

        // Assert - cts2 should be cancelled, not cts1
        cts1.IsCancellationRequested.ShouldBeFalse();
        cts2.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void MultipleBuilds_TrackedIndependently()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();
        using var cts1 = new CancellationTokenSource();
        using var cts2 = new CancellationTokenSource();
        using var cts3 = new CancellationTokenSource();
        registry.Register(1, cts1);
        registry.Register(2, cts2);
        registry.Register(3, cts3);

        // Act
        registry.TryCancel(2);
        registry.Unregister(3);

        // Assert
        registry.IsRunning(1).ShouldBeTrue();
        registry.IsRunning(2).ShouldBeTrue(); // Still running, just cancelled
        registry.IsRunning(3).ShouldBeFalse();

        cts1.IsCancellationRequested.ShouldBeFalse();
        cts2.IsCancellationRequested.ShouldBeTrue();
        cts3.IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public void Registry_IsThreadSafe()
    {
        // Arrange
        var registry = new CancellationTokenRegistry();
        var exceptions = new List<Exception>();

        // Act - perform concurrent operations
        Parallel.For(0, 100, i =>
        {
            try
            {
                using var cts = new CancellationTokenSource();
                registry.Register(i, cts);
                registry.IsRunning(i);
                registry.TryCancel(i);
                registry.Unregister(i);
            }
            catch (Exception ex)
            {
                lock (exceptions)
                {
                    exceptions.Add(ex);
                }
            }
        });

        // Assert - no exceptions during concurrent access
        exceptions.ShouldBeEmpty();
    }
}

/// <summary>
/// Tests for GitHubService commit status and clone operations.
/// Uses the mock service to verify call patterns.
/// </summary>
public class GitHubServiceMockTests
{
    [Fact]
    public void MockGitHubService_RecordsCommitStatusCalls()
    {
        // Arrange
        var service = new Ando.Server.Tests.TestFixtures.MockGitHubService();

        // Act
        service.SetCommitStatusAsync(
            123,
            "owner/repo",
            "abc123",
            Ando.Server.GitHub.CommitStatusState.Pending,
            "Build started").Wait();

        // Assert
        service.CommitStatusCalls.ShouldHaveSingleItem();
        var call = service.CommitStatusCalls[0];
        call.InstallationId.ShouldBe(123);
        call.RepoFullName.ShouldBe("owner/repo");
        call.CommitSha.ShouldBe("abc123");
        call.State.ShouldBe(Ando.Server.GitHub.CommitStatusState.Pending);
        call.Description.ShouldBe("Build started");
    }

    [Fact]
    public void MockGitHubService_RecordsCloneRepoCalls()
    {
        // Arrange
        var service = new Ando.Server.Tests.TestFixtures.MockGitHubService();

        // Act
        service.CloneRepositoryAsync(
            123,
            "owner/repo",
            "main",
            "abc123",
            "/tmp/repos/abc123").Wait();

        // Assert
        service.CloneRepoCalls.ShouldHaveSingleItem();
        var call = service.CloneRepoCalls[0];
        call.InstallationId.ShouldBe(123);
        call.RepoFullName.ShouldBe("owner/repo");
        call.Branch.ShouldBe("main");
        call.CommitSha.ShouldBe("abc123");
        call.TargetDirectory.ShouldBe("/tmp/repos/abc123");
    }

    [Fact]
    public void MockGitHubService_ThrowsOnCloneWhenConfigured()
    {
        // Arrange
        var service = new Ando.Server.Tests.TestFixtures.MockGitHubService
        {
            ThrowOnCloneRepo = new Exception("Clone failed")
        };

        // Act & Assert
        Should.Throw<Exception>(() =>
            service.CloneRepositoryAsync(123, "owner/repo", "main", "abc123", "/tmp").Wait())
            .Message.ShouldBe("Clone failed");
    }

    [Fact]
    public void MockGitHubService_ReturnsInstallationToken()
    {
        // Arrange
        var service = new Ando.Server.Tests.TestFixtures.MockGitHubService
        {
            MockInstallationToken = "test-token-xyz"
        };

        // Act
        var token = service.GetInstallationTokenAsync(123).Result;

        // Assert
        token.ShouldBe("test-token-xyz");
    }
}

/// <summary>
/// Tests for BuildService mock operations.
/// </summary>
public class BuildServiceMockTests
{
    [Fact]
    public void MockBuildService_RecordsQueueBuildCalls()
    {
        // Arrange
        var service = new Ando.Server.Tests.TestFixtures.MockBuildService();

        // Act
        var buildId = service.QueueBuildAsync(
            projectId: 1,
            commitSha: "abc123",
            branch: "main",
            trigger: Ando.Server.Models.BuildTrigger.Push,
            commitMessage: "Test commit",
            commitAuthor: "Test Author").Result;

        // Assert
        service.QueueBuildCalls.ShouldHaveSingleItem();
        var call = service.QueueBuildCalls[0];
        call.ProjectId.ShouldBe(1);
        call.CommitSha.ShouldBe("abc123");
        call.Branch.ShouldBe("main");
        call.Trigger.ShouldBe(Ando.Server.Models.BuildTrigger.Push);
        call.CommitMessage.ShouldBe("Test commit");
        call.CommitAuthor.ShouldBe("Test Author");
        buildId.ShouldBe(1); // First build ID
    }

    [Fact]
    public void MockBuildService_IncrementsNextBuildId()
    {
        // Arrange
        var service = new Ando.Server.Tests.TestFixtures.MockBuildService
        {
            NextBuildId = 100
        };

        // Act
        var buildId1 = service.QueueBuildAsync(1, "abc", "main", Ando.Server.Models.BuildTrigger.Push).Result;
        var buildId2 = service.QueueBuildAsync(1, "def", "main", Ando.Server.Models.BuildTrigger.Push).Result;

        // Assert
        buildId1.ShouldBe(100);
        buildId2.ShouldBe(101);
    }

    [Fact]
    public void MockBuildService_RecordsCancelBuildCalls()
    {
        // Arrange
        var service = new Ando.Server.Tests.TestFixtures.MockBuildService();

        // Act
        service.CancelBuildAsync(42).Wait();

        // Assert
        service.CancelBuildCalls.ShouldHaveSingleItem();
        service.CancelBuildCalls[0].ShouldBe(42);
    }

    [Fact]
    public void MockBuildService_RecordsRetryBuildCalls()
    {
        // Arrange
        var service = new Ando.Server.Tests.TestFixtures.MockBuildService
        {
            NextBuildId = 50
        };

        // Act
        var newBuildId = service.RetryBuildAsync(42).Result;

        // Assert
        service.RetryBuildCalls.ShouldHaveSingleItem();
        service.RetryBuildCalls[0].ShouldBe(42);
        newBuildId.ShouldBe(50);
    }
}
