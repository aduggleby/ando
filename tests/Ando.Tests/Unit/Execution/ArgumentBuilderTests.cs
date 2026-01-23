// =============================================================================
// ArgumentBuilderTests.cs
//
// Summary: Unit tests for ArgumentBuilder fluent class.
//
// Tests verify argument addition, conditional addition, flag handling,
// method chaining, and array building behavior.
// =============================================================================

using Ando.Execution;
using Shouldly;

namespace Ando.Tests.Unit.Execution;

[Trait("Category", "Unit")]
public class ArgumentBuilderTests
{
    [Fact]
    public void Add_SingleArg_AddsToList()
    {
        var builder = new ArgumentBuilder();

        var result = builder.Add("arg1").Build();

        result.ShouldBe(["arg1"]);
    }

    [Fact]
    public void Add_MultipleArgs_AddsAllToList()
    {
        var builder = new ArgumentBuilder();

        var result = builder.Add("arg1", "arg2", "arg3").Build();

        result.ShouldBe(["arg1", "arg2", "arg3"]);
    }

    [Fact]
    public void Add_CalledMultipleTimes_PreservesOrder()
    {
        var builder = new ArgumentBuilder();

        var result = builder
            .Add("first")
            .Add("second")
            .Add("third")
            .Build();

        result.ShouldBe(["first", "second", "third"]);
    }

    [Fact]
    public void AddIf_ConditionTrue_AddsArgs()
    {
        var builder = new ArgumentBuilder();

        var result = builder.AddIf(true, "--flag", "value").Build();

        result.ShouldBe(["--flag", "value"]);
    }

    [Fact]
    public void AddIf_ConditionFalse_DoesNotAddArgs()
    {
        var builder = new ArgumentBuilder();

        var result = builder.AddIf(false, "--flag", "value").Build();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void AddIfNotNull_FlagAndValue_WithValue_AddsBoth()
    {
        var builder = new ArgumentBuilder();

        var result = builder.AddIfNotNull("--config", "Release").Build();

        result.ShouldBe(["--config", "Release"]);
    }

    [Fact]
    public void AddIfNotNull_FlagAndValue_WithNull_DoesNotAdd()
    {
        var builder = new ArgumentBuilder();

        var result = builder.AddIfNotNull("--config", null).Build();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void AddIfNotNull_SingleValue_WhenNotNull_AddsValue()
    {
        var builder = new ArgumentBuilder();

        var result = builder.AddIfNotNull("someValue").Build();

        result.ShouldBe(["someValue"]);
    }

    [Fact]
    public void AddIfNotNull_SingleValue_WhenNull_SkipsValue()
    {
        var builder = new ArgumentBuilder();
        string? nullValue = null;

        var result = builder.AddIfNotNull(nullValue).Build();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void AddIfNotNull_NullableStruct_WhenHasValue_AddsFlagAndValue()
    {
        var builder = new ArgumentBuilder();
        int? value = 42;

        var result = builder.AddIfNotNull("--count", value).Build();

        result.ShouldBe(["--count", "42"]);
    }

    [Fact]
    public void AddIfNotNull_NullableStruct_WhenNull_DoesNotAdd()
    {
        var builder = new ArgumentBuilder();
        int? value = null;

        var result = builder.AddIfNotNull("--count", value).Build();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void AddFlag_ConditionTrue_AddsFlag()
    {
        var builder = new ArgumentBuilder();

        var result = builder.AddFlag(true, "--verbose").Build();

        result.ShouldBe(["--verbose"]);
    }

    [Fact]
    public void AddFlag_ConditionFalse_DoesNotAddFlag()
    {
        var builder = new ArgumentBuilder();

        var result = builder.AddFlag(false, "--verbose").Build();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void Build_EmptyBuilder_ReturnsEmptyArray()
    {
        var builder = new ArgumentBuilder();

        var result = builder.Build();

        result.ShouldBeEmpty();
    }

    [Fact]
    public void MethodChaining_AllMethodsReturnBuilder()
    {
        var builder = new ArgumentBuilder();

        // All methods should return the same builder instance for chaining
        var result = builder
            .Add("cmd")
            .AddIf(true, "--flag")
            .AddIfNotNull("--config", "Release")
            .AddIfNotNull("value")
            .AddFlag(true, "--verbose");

        result.ShouldBeSameAs(builder);
    }

    [Fact]
    public void ComplexChain_BuildsCorrectArgs()
    {
        var builder = new ArgumentBuilder();
        var config = "Release";
        string? nullRuntime = null;
        var noRestore = true;
        var noBuild = false;

        var result = builder
            .Add("publish", "project.csproj")
            .AddIfNotNull("-c", config)
            .AddIfNotNull("-r", nullRuntime)
            .AddFlag(noRestore, "--no-restore")
            .AddFlag(noBuild, "--no-build")
            .AddIf(config == "Release", "-p:PublishTrimmed=true")
            .Build();

        result.ShouldBe([
            "publish", "project.csproj",
            "-c", "Release",
            "--no-restore",
            "-p:PublishTrimmed=true"
        ]);
    }

    [Fact]
    public void Build_CanBeCalledMultipleTimes_ReturnsSameResult()
    {
        var builder = new ArgumentBuilder().Add("arg1", "arg2");

        var result1 = builder.Build();
        var result2 = builder.Build();

        result1.ShouldBe(result2);
    }

    [Fact]
    public void Build_ReturnsNewArrayEachTime()
    {
        var builder = new ArgumentBuilder().Add("arg1");

        var result1 = builder.Build();
        var result2 = builder.Build();

        result1.ShouldNotBeSameAs(result2);
    }
}
