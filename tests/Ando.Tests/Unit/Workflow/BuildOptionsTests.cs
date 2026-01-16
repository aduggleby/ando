// =============================================================================
// BuildOptionsTests.cs
//
// Summary: Unit tests for BuildOptions class.
//
// Tests verify that:
// - Image property is publicly readable
// - Image defaults to null (uses CLI default image)
//
// Note: UseImage is internal and tested indirectly through AndoOperations.
// =============================================================================

using Ando.Workflow;

namespace Ando.Tests.Unit.Workflow;

[Trait("Category", "Unit")]
public class BuildOptionsTests
{
    [Fact]
    public void Image_DefaultsToNull()
    {
        var options = new BuildOptions();

        Assert.Null(options.Image);
    }

    [Fact]
    public void Configuration_EnumHasExpectedValues()
    {
        // Verify the Configuration enum exists and has expected values
        Assert.Equal(0, (int)Configuration.Debug);
        Assert.Equal(1, (int)Configuration.Release);
    }
}
