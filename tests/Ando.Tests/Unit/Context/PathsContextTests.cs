// =============================================================================
// PathsContextTests.cs
//
// Summary: Unit tests for PathsContext class.
//
// Tests verify root path initialization, artifacts path derivation,
// and path combination behavior.
// =============================================================================

using Ando.Context;

namespace Ando.Tests.Unit.Context;

[Trait("Category", "Unit")]
public class PathsContextTests
{
    [Fact]
    public void Constructor_SetsRootPath()
    {
        var context = new PathsContext("/home/test");
        Assert.Equal("/home/test", context.Root.Value);
    }

    [Fact]
    public void Src_IsSubdirectoryOfRoot()
    {
        var context = new PathsContext("/home/test");
        Assert.Equal(Path.Combine("/home/test", "src"), context.Src.Value);
    }

    [Fact]
    public void Artifacts_IsSubdirectoryOfRoot()
    {
        var context = new PathsContext("/home/test");
        Assert.Equal(Path.Combine("/home/test", "artifacts"), context.Artifacts.Value);
    }

    [Fact]
    public void Temp_IsInAndoDirectory()
    {
        var context = new PathsContext("/home/test");
        Assert.Equal(Path.Combine("/home/test", ".ando", "tmp"), context.Temp.Value);
    }

    [Fact]
    public void EnsureDirectoriesExist_CreatesDirectories()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var context = new PathsContext(tempRoot);
            context.EnsureDirectoriesExist();

            Assert.True(Directory.Exists(context.Artifacts.Value));
            Assert.True(Directory.Exists(context.Temp.Value));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [Fact]
    public void EnsureDirectoriesExist_IsIdempotent()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var context = new PathsContext(tempRoot);
            context.EnsureDirectoriesExist();
            context.EnsureDirectoriesExist(); // Should not throw

            Assert.True(Directory.Exists(context.Artifacts.Value));
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }
}
