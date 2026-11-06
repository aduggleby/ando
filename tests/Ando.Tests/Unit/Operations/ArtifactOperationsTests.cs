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
    public void CopyToHost_KeepsLeadingDotOfHiddenContainerPath()
    {
        var artifacts = CreateArtifacts();

        artifacts.CopyToHost(".run/out", "out");
        artifacts.CopyZippedToHost(".run/zip", "zip");

        Assert.Equal("/workspace/.run/out", artifacts.Artifacts[0].ContainerPath);
        Assert.Equal("/workspace/.run/zip", artifacts.ZippedArtifacts[0].ContainerPath);
    }

    [Theory]
    [InlineData(".run/release/abc1234", "repo/.run/release/abc1234")]
    [InlineData("./out", "repo/out")]
    [InlineData("../sibling", "sibling")]
    public void ResolveHostPath_KeepsLeadingDots(string hostPath, string expectedUnderBase)
    {
        var baseDir = Path.GetFullPath(Path.GetTempPath());
        var projectRoot = Path.Combine(baseDir, "repo");

        Assert.Equal(Path.GetFullPath(Path.Combine(baseDir, expectedUnderBase)),
            ArtifactOperations.ResolveHostPath(projectRoot, hostPath));
    }

    [Fact]
    public void ResolveHostPath_KeepsRootedPath()
    {
        var rooted = Path.Combine(Path.GetTempPath(), "abs", "out");

        Assert.Equal(rooted, ArtifactOperations.ResolveHostPath(Path.Combine(Path.GetTempPath(), "repo"), rooted));
    }

    [Theory]
    [InlineData(".", true)]
    [InlineData("./", true)]
    [InlineData("..", true)]
    [InlineData("../..", true)]
    [InlineData("out", false)]
    [InlineData("../sibling", false)]
    [InlineData("../repo-other", false)]
    public void IsProjectRootOrAncestor_RefusesRootAndParents(string hostPath, bool expected)
    {
        var projectRoot = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "a", "repo");
        var resolved = ArtifactOperations.ResolveHostPath(projectRoot, hostPath);

        Assert.Equal(expected, ArtifactOperations.IsProjectRootOrAncestor(projectRoot, resolved));
    }

    [Fact]
    public void IsProjectRootOrAncestor_RefusesFilesystemRoot()
    {
        var projectRoot = Path.Combine(Path.GetFullPath(Path.GetTempPath()), "repo");

        Assert.True(ArtifactOperations.IsProjectRootOrAncestor(projectRoot, Path.GetPathRoot(projectRoot)!));
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
