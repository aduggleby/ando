// =============================================================================
// SemVerTests.cs
//
// Unit tests for SemVer parsing and bumping functionality.
// =============================================================================

using Ando.Versioning;
using Shouldly;

namespace Ando.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class SemVerTests
{
    [Theory]
    [InlineData("1.2.3", 1, 2, 3, null)]
    [InlineData("0.0.0", 0, 0, 0, null)]
    [InlineData("10.20.30", 10, 20, 30, null)]
    [InlineData("1.2.3-beta", 1, 2, 3, "beta")]
    [InlineData("1.2.3-beta.1", 1, 2, 3, "beta.1")]
    [InlineData("1.2.3-alpha.1.2.3", 1, 2, 3, "alpha.1.2.3")]
    [InlineData("v1.2.3", 1, 2, 3, null)]
    [InlineData("V1.2.3", 1, 2, 3, null)]
    [InlineData("v1.2.3-beta", 1, 2, 3, "beta")]
    public void Parse_ValidVersion_ReturnsCorrectComponents(
        string input, int major, int minor, int patch, string? prerelease)
    {
        var result = SemVer.Parse(input);

        result.Major.ShouldBe(major);
        result.Minor.ShouldBe(minor);
        result.Patch.ShouldBe(patch);
        result.Prerelease.ShouldBe(prerelease);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("invalid")]
    [InlineData("1.2")]
    [InlineData("1.2.3.4")]
    [InlineData("a.b.c")]
    [InlineData("1.2.")]
    [InlineData(".2.3")]
    [InlineData("1..3")]
    public void Parse_InvalidVersion_ThrowsArgumentException(string input)
    {
        Should.Throw<ArgumentException>(() => SemVer.Parse(input));
    }

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData("invalid", false)]
    [InlineData("1.2.3-beta", true)]
    [InlineData("", false)]
    public void TryParse_ReturnsExpectedResult(string input, bool expectedSuccess)
    {
        var success = SemVer.TryParse(input, out var result);

        success.ShouldBe(expectedSuccess);
        if (expectedSuccess)
            result.ShouldNotBeNull();
    }

    [Theory]
    [InlineData("1.2.3", BumpType.Patch, "1.2.4")]
    [InlineData("1.2.3", BumpType.Minor, "1.3.0")]
    [InlineData("1.2.3", BumpType.Major, "2.0.0")]
    [InlineData("0.0.0", BumpType.Patch, "0.0.1")]
    [InlineData("0.0.0", BumpType.Minor, "0.1.0")]
    [InlineData("0.0.0", BumpType.Major, "1.0.0")]
    [InlineData("1.9.9", BumpType.Patch, "1.9.10")]
    [InlineData("1.9.9", BumpType.Minor, "1.10.0")]
    public void Bump_ReturnsCorrectVersion(string input, BumpType type, string expected)
    {
        var version = SemVer.Parse(input);
        var bumped = version.Bump(type);

        bumped.ToString().ShouldBe(expected);
    }

    [Theory]
    [InlineData("1.2.3-beta", BumpType.Patch, "1.2.4")]
    [InlineData("1.2.3-beta.1", BumpType.Minor, "1.3.0")]
    [InlineData("1.2.3-alpha", BumpType.Major, "2.0.0")]
    public void Bump_RemovesPrerelease(string input, BumpType type, string expected)
    {
        var version = SemVer.Parse(input);
        var bumped = version.Bump(type);

        bumped.ToString().ShouldBe(expected);
        bumped.Prerelease.ShouldBeNull();
    }

    [Fact]
    public void Bump_DoesNotModifyOriginal()
    {
        var original = SemVer.Parse("1.2.3");
        var bumped = original.Bump(BumpType.Patch);

        original.ToString().ShouldBe("1.2.3");
        bumped.ToString().ShouldBe("1.2.4");
    }

    [Fact]
    public void ToString_WithoutPrerelease_FormatsCorrectly()
    {
        var version = new SemVer(1, 2, 3);
        version.ToString().ShouldBe("1.2.3");
    }

    [Fact]
    public void ToString_WithPrerelease_FormatsCorrectly()
    {
        var version = new SemVer(1, 2, 3, "beta.1");
        version.ToString().ShouldBe("1.2.3-beta.1");
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = SemVer.Parse("1.2.3");
        var b = SemVer.Parse("1.2.3");

        a.ShouldBe(b);
        (a == b).ShouldBeTrue();
    }

    [Fact]
    public void Equality_DifferentValues_AreNotEqual()
    {
        var a = SemVer.Parse("1.2.3");
        var b = SemVer.Parse("1.2.4");

        a.ShouldNotBe(b);
        (a != b).ShouldBeTrue();
    }

    [Fact]
    public void Equality_SameVersionDifferentPrerelease_AreNotEqual()
    {
        var a = SemVer.Parse("1.2.3");
        var b = SemVer.Parse("1.2.3-beta");

        a.ShouldNotBe(b);
    }
}
