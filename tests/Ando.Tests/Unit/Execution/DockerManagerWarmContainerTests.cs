// =============================================================================
// DockerManagerWarmContainerTests.cs
//
// Summary: Unit tests for DockerManager warm-container reuse decisions.
//
// These tests verify that warm containers are reused only when their immutable
// creation settings still match the requested build profile. They use an
// overridable DockerManager seam so no real Docker daemon is required.
//
// Design Decisions:
// - Exercise EnsureContainerAsync directly to cover the production decision flow
// - Override Docker lifecycle methods to avoid shelling out to Docker
// - Focus on image matching because container names are shared across profiles
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Execution;

[Trait("Category", "Unit")]
public class DockerManagerWarmContainerTests
{
    [Fact]
    public async Task EnsureContainerAsync_RecreatesWarmContainer_WhenImageDiffers()
    {
        var logger = new TestLogger();
        var manager = new TestDockerManager(logger)
        {
            ExistingContainer = new ContainerInfo("old-container", "ando-warm", true),
            ExistingImage = "node:22"
        };

        var result = await manager.EnsureContainerAsync(new ContainerConfig
        {
            Name = "ando-warm",
            Image = "custom-azure-cli:latest",
            ProjectRoot = "/workspace"
        });

        result.Id.ShouldBe("new-container");
        manager.RemovedContainers.ShouldContain("ando-warm");
        manager.CreatedConfigs.ShouldHaveSingleItem().Image.ShouldBe("custom-azure-cli:latest");
        manager.CopyRequests.ShouldBeEmpty();
        logger.InfoMessages.ShouldContain(message =>
            message.Contains("image 'node:22'") &&
            message.Contains("requested image 'custom-azure-cli:latest'"));
    }

    [Fact]
    public async Task EnsureContainerAsync_RecreatesWarmContainer_WhenImageCannotBeInspected()
    {
        var manager = new TestDockerManager(new TestLogger())
        {
            ExistingContainer = new ContainerInfo("old-container", "ando-warm", true),
            ExistingImage = null
        };

        var result = await manager.EnsureContainerAsync(new ContainerConfig
        {
            Name = "ando-warm",
            Image = "custom-azure-cli:latest",
            ProjectRoot = "/workspace"
        });

        result.Id.ShouldBe("new-container");
        manager.RemovedContainers.ShouldContain("ando-warm");
        manager.CreatedConfigs.ShouldHaveSingleItem().Image.ShouldBe("custom-azure-cli:latest");
        manager.CopyRequests.ShouldBeEmpty();
    }

    [Fact]
    public async Task EnsureContainerAsync_ReusesWarmContainer_WhenImageMatches()
    {
        var manager = new TestDockerManager(new TestLogger())
        {
            ExistingContainer = new ContainerInfo("old-container", "ando-warm", true),
            ExistingImage = "custom-azure-cli:latest"
        };

        var result = await manager.EnsureContainerAsync(new ContainerConfig
        {
            Name = "ando-warm",
            Image = "custom-azure-cli:latest",
            ProjectRoot = "/workspace"
        });

        result.Id.ShouldBe("old-container");
        manager.RemovedContainers.ShouldBeEmpty();
        manager.CreatedConfigs.ShouldBeEmpty();
        manager.CopyRequests.ShouldHaveSingleItem().ShouldBe(("old-container", "/workspace"));
    }

    private sealed class TestDockerManager(IBuildLogger logger) : DockerManager(logger)
    {
        public ContainerInfo? ExistingContainer { get; set; }
        public string? ExistingImage { get; set; }
        public List<string> RemovedContainers { get; } = [];
        public List<ContainerConfig> CreatedConfigs { get; } = [];
        public List<(string ContainerId, string ProjectRoot)> CopyRequests { get; } = [];

        public override Task<ContainerInfo?> FindWarmContainerAsync(string containerName)
        {
            return Task.FromResult(ExistingContainer);
        }

        protected override Task<string?> GetContainerImageAsync(string containerId)
        {
            return Task.FromResult(ExistingImage);
        }

        protected override Task<ContainerInfo> CreateContainerAsync(ContainerConfig config)
        {
            CreatedConfigs.Add(config);
            return Task.FromResult(new ContainerInfo("new-container", config.Name, true));
        }

        public override Task CopyProjectToContainerAsync(string containerId, string projectRoot)
        {
            CopyRequests.Add((containerId, projectRoot));
            return Task.CompletedTask;
        }

        public override Task RemoveContainerAsync(string containerName)
        {
            RemovedContainers.Add(containerName);
            return Task.CompletedTask;
        }
    }
}
