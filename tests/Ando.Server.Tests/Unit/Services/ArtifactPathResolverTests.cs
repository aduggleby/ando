// =============================================================================
// ArtifactPathResolverTests.cs
//
// Summary: Unit tests for ArtifactPathResolver.
//
// Verifies resolution of relative artifact storage paths and root boundary
// validation used by download and cleanup flows.
// =============================================================================

using Ando.Server.Services;

namespace Ando.Server.Tests.Unit.Services;

public class ArtifactPathResolverTests
{
    [Fact]
    public void ResolveAbsolutePath_WithRelativePath_CombinesWithRoot()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "ando-artifacts-root");
        var relativePath = Path.Combine("12", "34", "artifact.zip");

        // Act
        var resolved = ArtifactPathResolver.ResolveAbsolutePath(root, relativePath);

        // Assert
        resolved.ShouldBe(Path.GetFullPath(Path.Combine(root, relativePath)));
    }

    [Fact]
    public void IsWithinRoot_WithChildPath_ReturnsTrue()
    {
        // Arrange
        var root = Path.Combine(Path.GetTempPath(), "ando-artifacts-root");
        var filePath = Path.GetFullPath(Path.Combine(root, "12", "34", "artifact.zip"));

        // Act
        var isWithinRoot = ArtifactPathResolver.IsWithinRoot(root, filePath);

        // Assert
        isWithinRoot.ShouldBeTrue();
    }

    [Fact]
    public void IsWithinRoot_WithSiblingPath_ReturnsFalse()
    {
        // Arrange
        var baseDir = Path.Combine(Path.GetTempPath(), "ando-artifacts-root");
        var outsidePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(baseDir)!, "outside", "artifact.zip"));

        // Act
        var isWithinRoot = ArtifactPathResolver.IsWithinRoot(baseDir, outsidePath);

        // Assert
        isWithinRoot.ShouldBeFalse();
    }
}
