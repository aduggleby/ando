// =============================================================================
// BuildOptionsTests.cs
//
// Summary: Unit tests for BuildOptions class.
//
// Tests verify that:
// - Default configuration is Debug
// - UseConfiguration changes configuration
// - Fluent API returns self for chaining
// =============================================================================

using Ando.Workflow;

namespace Ando.Tests.Unit.Workflow;

[Trait("Category", "Unit")]
public class BuildOptionsTests
{
    [Fact]
    public void Configuration_DefaultsToDebug()
    {
        var options = new BuildOptions();

        Assert.Equal(Configuration.Debug, options.Configuration);
    }

    [Fact]
    public void UseConfiguration_SetsConfiguration()
    {
        var options = new BuildOptions();

        options.UseConfiguration(Configuration.Release);

        Assert.Equal(Configuration.Release, options.Configuration);
    }

    [Fact]
    public void UseConfiguration_ReturnsSelf_ForChaining()
    {
        var options = new BuildOptions();

        var result = options.UseConfiguration(Configuration.Release);

        Assert.Same(options, result);
    }

    [Fact]
    public void UseConfiguration_CanBeChangedMultipleTimes()
    {
        var options = new BuildOptions();

        options.UseConfiguration(Configuration.Release);
        options.UseConfiguration(Configuration.Debug);

        Assert.Equal(Configuration.Debug, options.Configuration);
    }

    [Theory]
    [InlineData(Configuration.Debug)]
    [InlineData(Configuration.Release)]
    public void UseConfiguration_AcceptsAllConfigurationValues(Configuration config)
    {
        var options = new BuildOptions();

        options.UseConfiguration(config);

        Assert.Equal(config, options.Configuration);
    }
}
