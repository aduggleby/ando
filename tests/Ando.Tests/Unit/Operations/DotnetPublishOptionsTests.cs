// =============================================================================
// DotnetPublishOptionsTests.cs
//
// Summary: Unit tests for DotnetPublishOptions fluent builder class.
//
// Tests verify that:
// - Each builder method sets the correct property
// - Builder methods return the same instance for chaining
// - All options can be combined in a fluent chain
// =============================================================================

using Ando.Context;
using Ando.Operations;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class DotnetPublishOptionsTests
{
    [Fact]
    public void Default_HasNullProperties()
    {
        var options = new DotnetPublishOptions();

        options.OutputPath.ShouldBeNull();
        options.Configuration.ShouldBeNull();
        options.Runtime.ShouldBeNull();
        options.SelfContained.ShouldBeNull();
        options.SingleFile.ShouldBeFalse();
        options.NoRestore.ShouldBeFalse();
        options.NoBuild.ShouldBeFalse();
    }

    [Fact]
    public void Output_BuildPath_SetsOutputPath()
    {
        var options = new DotnetPublishOptions();
        var path = new BuildPath("./dist/output");

        options.Output(path);

        options.OutputPath.HasValue.ShouldBeTrue();
        options.OutputPath!.Value.Value.ShouldEndWith("dist/output");
    }

    [Fact]
    public void Output_String_SetsOutputPath()
    {
        var options = new DotnetPublishOptions();

        options.Output("./dist/output");

        options.OutputPath.HasValue.ShouldBeTrue();
        options.OutputPath!.Value.Value.ShouldEndWith("dist/output");
    }

    [Fact]
    public void WithConfiguration_SetsConfiguration()
    {
        var options = new DotnetPublishOptions();

        options.WithConfiguration(Configuration.Release);

        options.Configuration.ShouldBe(Configuration.Release);
    }

    [Fact]
    public void WithRuntime_SetsRuntime()
    {
        var options = new DotnetPublishOptions();

        options.WithRuntime("linux-x64");

        options.Runtime.ShouldBe("linux-x64");
    }

    [Fact]
    public void AsSelfContained_True_SetsSelfContainedTrue()
    {
        var options = new DotnetPublishOptions();

        options.AsSelfContained();

        options.SelfContained.ShouldBe(true);
    }

    [Fact]
    public void AsSelfContained_False_SetsSelfContainedFalse()
    {
        var options = new DotnetPublishOptions();

        options.AsSelfContained(false);

        options.SelfContained.ShouldBe(false);
    }

    [Fact]
    public void AsSingleFile_SetsSingleFile()
    {
        var options = new DotnetPublishOptions();

        options.AsSingleFile();

        options.SingleFile.ShouldBeTrue();
    }

    [Fact]
    public void AsSingleFile_False_SetsSingleFileFalse()
    {
        var options = new DotnetPublishOptions();

        options.AsSingleFile(false);

        options.SingleFile.ShouldBeFalse();
    }

    [Fact]
    public void SkipRestore_SetsNoRestore()
    {
        var options = new DotnetPublishOptions();

        options.SkipRestore();

        options.NoRestore.ShouldBeTrue();
    }

    [Fact]
    public void SkipBuild_SetsNoBuild()
    {
        var options = new DotnetPublishOptions();

        options.SkipBuild();

        options.NoBuild.ShouldBeTrue();
    }

    [Fact]
    public void AllMethods_ReturnSameInstance_ForChaining()
    {
        var options = new DotnetPublishOptions();

        var result1 = options.Output("/dist");
        var result2 = options.WithConfiguration(Configuration.Release);
        var result3 = options.WithRuntime("linux-x64");
        var result4 = options.AsSelfContained();
        var result5 = options.AsSingleFile();
        var result6 = options.SkipRestore();
        var result7 = options.SkipBuild();

        result1.ShouldBeSameAs(options);
        result2.ShouldBeSameAs(options);
        result3.ShouldBeSameAs(options);
        result4.ShouldBeSameAs(options);
        result5.ShouldBeSameAs(options);
        result6.ShouldBeSameAs(options);
        result7.ShouldBeSameAs(options);
    }

    [Fact]
    public void FluentChain_SetsAllProperties()
    {
        var options = new DotnetPublishOptions()
            .Output("./dist/myapp")
            .WithConfiguration(Configuration.Release)
            .WithRuntime("win-x64")
            .AsSelfContained()
            .AsSingleFile()
            .SkipRestore()
            .SkipBuild();

        options.OutputPath!.Value.Value.ShouldEndWith("dist/myapp");
        options.Configuration.ShouldBe(Configuration.Release);
        options.Runtime.ShouldBe("win-x64");
        options.SelfContained.ShouldBe(true);
        options.SingleFile.ShouldBeTrue();
        options.NoRestore.ShouldBeTrue();
        options.NoBuild.ShouldBeTrue();
    }
}
