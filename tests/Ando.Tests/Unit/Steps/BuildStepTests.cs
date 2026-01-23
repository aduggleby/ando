// =============================================================================
// BuildStepTests.cs
//
// Summary: Unit tests for BuildStep class.
//
// Tests verify step name handling, context formatting, display name generation,
// and execution behavior.
// =============================================================================

using Ando.Steps;

namespace Ando.Tests.Unit.Steps;

[Trait("Category", "Unit")]
public class BuildStepTests
{
    [Fact]
    public void DisplayName_WithoutContext_ReturnsName()
    {
        var step = new BuildStep("Test.Step", () => Task.FromResult(true));

        Assert.Equal("Test.Step", step.DisplayName);
    }

    [Fact]
    public void DisplayName_WithContext_ReturnsNameAndContext()
    {
        var step = new BuildStep("Test.Step", () => Task.FromResult(true), "MyProject");

        Assert.Equal("Test.Step (MyProject)", step.DisplayName);
    }

    [Fact]
    public async Task Execute_RunsFunction()
    {
        var executed = false;
        var step = new BuildStep("Test.Step", () =>
        {
            executed = true;
            return Task.FromResult(true);
        });

        await step.Execute();

        Assert.True(executed);
    }

    [Fact]
    public async Task Execute_ReturnsResult()
    {
        var step = new BuildStep("Test.Step", () => Task.FromResult(false));

        var result = await step.Execute();

        Assert.False(result);
    }

    [Fact]
    public void Constructor_SetsNameAndContext()
    {
        var step = new BuildStep("MyStep", () => Task.FromResult(true), "MyContext");

        Assert.Equal("MyStep", step.Name);
        Assert.Equal("MyContext", step.Context);
    }

    [Fact]
    public void Constructor_NullContext_SetsNull()
    {
        var step = new BuildStep("MyStep", () => Task.FromResult(true));

        Assert.Null(step.Context);
    }

    [Fact]
    public async Task Execute_PropagatesException()
    {
        var step = new BuildStep("Failing", () =>
        {
            throw new InvalidOperationException("Test error");
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => step.Execute());
    }
}
