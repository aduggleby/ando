// =============================================================================
// BuildPathTests.cs
//
// Summary: Unit tests for BuildPath struct.
//
// Tests verify path normalization, division operator for combining paths,
// implicit string conversion, and equality behavior.
// =============================================================================

using Ando.Context;

namespace Ando.Tests.Unit.Context;

[Trait("Category", "Unit")]
public class BuildPathTests
{
    [Fact]
    public void Constructor_NormalizesPath()
    {
        var path = new BuildPath("./src");
        Assert.True(Path.IsPathRooted(path.Value));
    }

    [Fact]
    public void DivisionOperator_CombinesPaths()
    {
        var root = new BuildPath("/home/test");
        var combined = root / "subdir";

        Assert.Equal(Path.Combine("/home/test", "subdir"), combined.Value);
    }

    [Fact]
    public void DivisionOperator_ChainsCombinations()
    {
        var root = new BuildPath("/home/test");
        var combined = root / "subdir" / "file.txt";

        Assert.Equal(Path.Combine("/home/test", "subdir", "file.txt"), combined.Value);
    }

    [Fact]
    public void ImplicitConversion_ReturnsValue()
    {
        var path = new BuildPath("/home/test");
        string implicitValue = path;

        Assert.Equal(path.Value, implicitValue);
    }

    [Fact]
    public void ToString_ReturnsValue()
    {
        var path = new BuildPath("/home/test");
        Assert.Equal(path.Value, path.ToString());
    }

    [Fact]
    public void Equality_SamePathsAreEqual()
    {
        var path1 = new BuildPath("/home/test");
        var path2 = new BuildPath("/home/test");

        Assert.Equal(path1, path2);
    }

    [Fact]
    public void Equality_DifferentPathsAreNotEqual()
    {
        var path1 = new BuildPath("/home/test1");
        var path2 = new BuildPath("/home/test2");

        Assert.NotEqual(path1, path2);
    }

    [Fact]
    public void DivisionOperator_WithEmptyString_ReturnsSamePath()
    {
        var path = new BuildPath("/home/test");
        var combined = path / "";

        Assert.Equal(path.Value, combined.Value);
    }
}
