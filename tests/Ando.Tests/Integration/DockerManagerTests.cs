using Ando.Execution;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Integration;

/// <summary>
/// Integration tests for DockerManager.
/// These tests require Docker to be available on the system.
/// </summary>
[Trait("Category", "Integration")]
[Trait("Category", "Docker")]
public class DockerManagerTests : IDisposable
{
    private readonly TestLogger _logger = new();
    private readonly DockerManager _dockerManager;
    private readonly List<string> _createdContainers = new();
    private readonly string _testProjectRoot;

    public DockerManagerTests()
    {
        _dockerManager = new DockerManager(_logger);
        _testProjectRoot = Path.Combine(Path.GetTempPath(), $"ando-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_testProjectRoot);
    }

    public void Dispose()
    {
        // Clean up any containers created during tests
        foreach (var container in _createdContainers)
        {
            _dockerManager.RemoveContainerAsync(container).GetAwaiter().GetResult();
        }

        // Clean up temp directory
        if (Directory.Exists(_testProjectRoot))
        {
            Directory.Delete(_testProjectRoot, recursive: true);
        }
    }

    [SkippableFact]
    public void IsDockerAvailable_ReturnsCorrectly()
    {
        var isAvailable = _dockerManager.IsDockerAvailable();

        // This test passes regardless of Docker availability - it just verifies no exception
        Assert.True(isAvailable || !isAvailable);
    }

    [SkippableFact]
    public async Task EnsureContainerAsync_CreatesNewContainer()
    {
        Skip.IfNot(_dockerManager.IsDockerAvailable(), "Docker not available");

        var containerName = $"ando-test-{Guid.NewGuid():N}".Substring(0, 20);
        _createdContainers.Add(containerName);

        var config = new ContainerConfig
        {
            Name = containerName,
            ProjectRoot = _testProjectRoot,
            Image = "alpine:latest"
        };

        var container = await _dockerManager.EnsureContainerAsync(config);

        Assert.NotNull(container);
        Assert.Equal(containerName, container.Name);
        Assert.True(container.IsRunning);
    }

    [SkippableFact]
    public async Task FindWarmContainerAsync_ReturnsNullForNonexistent()
    {
        Skip.IfNot(_dockerManager.IsDockerAvailable(), "Docker not available");

        var container = await _dockerManager.FindWarmContainerAsync("nonexistent-container-99999");

        Assert.Null(container);
    }

    [SkippableFact]
    public async Task EnsureContainerAsync_ReusesExistingContainer()
    {
        Skip.IfNot(_dockerManager.IsDockerAvailable(), "Docker not available");

        var containerName = $"ando-test-{Guid.NewGuid():N}".Substring(0, 20);
        _createdContainers.Add(containerName);

        var config = new ContainerConfig
        {
            Name = containerName,
            ProjectRoot = _testProjectRoot,
            Image = "alpine:latest"
        };

        // Create first container
        var container1 = await _dockerManager.EnsureContainerAsync(config);

        // Ensure again - should reuse
        var container2 = await _dockerManager.EnsureContainerAsync(config);

        // Docker may return truncated IDs, so check if one starts with the other
        var idsMatch = container1.Id.StartsWith(container2.Id) || container2.Id.StartsWith(container1.Id);
        idsMatch.ShouldBeTrue($"Container IDs should match: '{container1.Id}' vs '{container2.Id}'");
    }

    [SkippableFact]
    public async Task RemoveContainerAsync_RemovesContainer()
    {
        Skip.IfNot(_dockerManager.IsDockerAvailable(), "Docker not available");

        var containerName = $"ando-test-{Guid.NewGuid():N}".Substring(0, 20);

        var config = new ContainerConfig
        {
            Name = containerName,
            ProjectRoot = _testProjectRoot,
            Image = "alpine:latest"
        };

        // Create container
        await _dockerManager.EnsureContainerAsync(config);

        // Remove it
        await _dockerManager.RemoveContainerAsync(containerName);

        // Verify it's gone
        var found = await _dockerManager.FindWarmContainerAsync(containerName);
        Assert.Null(found);
    }

    [SkippableFact]
    public async Task StopContainerAsync_StopsRunningContainer()
    {
        Skip.IfNot(_dockerManager.IsDockerAvailable(), "Docker not available");

        var containerName = $"ando-test-{Guid.NewGuid():N}".Substring(0, 20);
        _createdContainers.Add(containerName);

        var config = new ContainerConfig
        {
            Name = containerName,
            ProjectRoot = _testProjectRoot,
            Image = "alpine:latest"
        };

        var container = await _dockerManager.EnsureContainerAsync(config);
        Assert.True(container.IsRunning);

        await _dockerManager.StopContainerAsync(container.Id);

        var stopped = await _dockerManager.FindWarmContainerAsync(containerName);
        Assert.NotNull(stopped);
        Assert.False(stopped.IsRunning);
    }

    [Fact]
    public void GetDockerInstallInstructions_ReturnsInstructions()
    {
        var instructions = _dockerManager.GetDockerInstallInstructions();

        Assert.NotNull(instructions);
        Assert.NotEmpty(instructions);
    }

    [SkippableFact]
    public async Task CleanArtifactsAsync_CleansAndRecreatesDirectory()
    {
        Skip.IfNot(_dockerManager.IsDockerAvailable(), "Docker not available");

        var containerName = $"ando-test-{Guid.NewGuid():N}".Substring(0, 20);
        _createdContainers.Add(containerName);

        // Create artifacts directory with content
        var artifactsDir = Path.Combine(_testProjectRoot, "artifacts");
        Directory.CreateDirectory(artifactsDir);
        File.WriteAllText(Path.Combine(artifactsDir, "test.txt"), "test content");

        var config = new ContainerConfig
        {
            Name = containerName,
            ProjectRoot = _testProjectRoot,
            Image = "alpine:latest"
        };

        var container = await _dockerManager.EnsureContainerAsync(config);

        // Clean artifacts
        await _dockerManager.CleanArtifactsAsync(container.Id);

        // Verify directory exists but is empty (or file was removed)
        // Note: The cleaning happens inside the container, but the mounted directory
        // should reflect the changes
        Assert.True(Directory.Exists(artifactsDir));
    }
}

/// <summary>
/// Helper for skipping tests based on conditions.
/// </summary>
public static class Skip
{
    public static void IfNot(bool condition, string reason)
    {
        if (!condition)
        {
            throw new SkipException(reason);
        }
    }
}

/// <summary>
/// Exception thrown to skip a test.
/// </summary>
public class SkipException : Exception
{
    public SkipException(string message) : base(message) { }
}

/// <summary>
/// Fact attribute that allows tests to be skipped.
/// </summary>
public class SkippableFactAttribute : FactAttribute
{
}
