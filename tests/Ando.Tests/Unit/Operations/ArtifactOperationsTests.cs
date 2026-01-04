// =============================================================================
// ArtifactOperationsTests.cs
//
// Summary: Unit tests for ArtifactOperations class.
//
// Tests verify that:
// - Artifact registration works correctly
// - Container paths are normalized (relative to /workspace)
// - Clear removes all registered artifacts
// - Multiple artifacts can be registered
//
// Design: Uses TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class ArtifactOperationsTests
{
    private readonly TestLogger _logger = new();

    private ArtifactOperations CreateArtifacts() => new(_logger);

    [Fact]
    public void CopyToHost_RegistersArtifact()
    {
        var artifacts = CreateArtifacts();

        artifacts.CopyToHost("/workspace/dist", "./output");

        Assert.Single(artifacts.Artifacts);
        Assert.Equal("/workspace/dist", artifacts.Artifacts[0].ContainerPath);
        Assert.Equal("./output", artifacts.Artifacts[0].HostPath);
    }

    [Fact]
    public void CopyToHost_NormalizesRelativePath()
    {
        var artifacts = CreateArtifacts();

        artifacts.CopyToHost("dist", "./output");

        Assert.Single(artifacts.Artifacts);
        Assert.Equal("/workspace/dist", artifacts.Artifacts[0].ContainerPath);
    }

    [Fact]
    public void CopyToHost_NormalizesPathWithDotSlash()
    {
        var artifacts = CreateArtifacts();

        artifacts.CopyToHost("./dist", "./output");

        Assert.Single(artifacts.Artifacts);
        Assert.Equal("/workspace/dist", artifacts.Artifacts[0].ContainerPath);
    }

    [Fact]
    public void CopyToHost_PreservesAbsolutePath()
    {
        var artifacts = CreateArtifacts();

        artifacts.CopyToHost("/tmp/output", "./output");

        Assert.Single(artifacts.Artifacts);
        Assert.Equal("/tmp/output", artifacts.Artifacts[0].ContainerPath);
    }

    [Fact]
    public void CopyToHost_RegistersMultipleArtifacts()
    {
        var artifacts = CreateArtifacts();

        artifacts.CopyToHost("/workspace/dist", "./dist");
        artifacts.CopyToHost("/workspace/bin", "./bin");
        artifacts.CopyToHost("/tmp/logs", "./logs");

        Assert.Equal(3, artifacts.Artifacts.Count);
    }

    [Fact]
    public void CopyToHost_LogsRegistration()
    {
        var artifacts = CreateArtifacts();

        artifacts.CopyToHost("/workspace/dist", "./output");

        Assert.Contains(_logger.DebugMessages, m => m.Contains("Registered artifact"));
    }

    [Fact]
    public void Clear_RemovesAllArtifacts()
    {
        var artifacts = CreateArtifacts();
        artifacts.CopyToHost("/workspace/dist", "./dist");
        artifacts.CopyToHost("/workspace/bin", "./bin");

        artifacts.Clear();

        Assert.Empty(artifacts.Artifacts);
    }

    [Fact]
    public void Artifacts_ReturnsReadOnlyList()
    {
        var artifacts = CreateArtifacts();
        artifacts.CopyToHost("/workspace/dist", "./dist");

        var list = artifacts.Artifacts;

        Assert.IsAssignableFrom<IReadOnlyList<ArtifactEntry>>(list);
    }

    [Fact]
    public void ArtifactEntry_Record_HasExpectedValues()
    {
        var entry = new ArtifactEntry("/container/path", "/host/path");

        Assert.Equal("/container/path", entry.ContainerPath);
        Assert.Equal("/host/path", entry.HostPath);
    }

    [Fact]
    public void ArtifactEntry_Record_SupportsEquality()
    {
        var entry1 = new ArtifactEntry("/path", "./output");
        var entry2 = new ArtifactEntry("/path", "./output");
        var entry3 = new ArtifactEntry("/other", "./output");

        Assert.Equal(entry1, entry2);
        Assert.NotEqual(entry1, entry3);
    }

    [Fact]
    public void CopyToHost_WithNestedPath_NormalizesCorrectly()
    {
        var artifacts = CreateArtifacts();

        artifacts.CopyToHost("./src/output/release", "./dist");

        Assert.Equal("/workspace/src/output/release", artifacts.Artifacts[0].ContainerPath);
    }
}
