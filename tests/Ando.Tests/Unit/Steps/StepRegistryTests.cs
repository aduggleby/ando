// =============================================================================
// StepRegistryTests.cs
//
// Summary: Unit tests for StepRegistry class.
//
// Tests verify that:
// - Steps can be registered via convenience method or BuildStep object
// - Steps are stored in registration order
// - Context information is preserved
// - Clear() removes all steps
// - Steps collection is read-only
//
// Design: Pure unit tests with no external dependencies.
// =============================================================================

using Ando.Steps;

namespace Ando.Tests.Unit.Steps;

[Trait("Category", "Unit")]
public class StepRegistryTests
{
    [Fact]
    public void Register_AddsStepToCollection()
    {
        var registry = new StepRegistry();
        registry.Register("Test.Step", () => Task.FromResult(true));

        Assert.Single(registry.Steps);
        Assert.Equal("Test.Step", registry.Steps[0].Name);
    }

    [Fact]
    public void Register_WithContext_SetsContext()
    {
        var registry = new StepRegistry();
        registry.Register("Test.Step", () => Task.FromResult(true), "MyProject");

        Assert.Equal("MyProject", registry.Steps[0].Context);
    }

    [Fact]
    public void Register_BuildStep_AddsStepDirectly()
    {
        var registry = new StepRegistry();
        var step = new BuildStep("Direct.Step", () => Task.FromResult(true), "Context");
        registry.Register(step);

        Assert.Single(registry.Steps);
        Assert.Same(step, registry.Steps[0]);
    }

    [Fact]
    public void Clear_RemovesAllSteps()
    {
        var registry = new StepRegistry();
        registry.Register("Step1", () => Task.FromResult(true));
        registry.Register("Step2", () => Task.FromResult(true));

        registry.Clear();

        Assert.Empty(registry.Steps);
    }

    [Fact]
    public void Steps_PreservesOrder()
    {
        var registry = new StepRegistry();
        registry.Register("First", () => Task.FromResult(true));
        registry.Register("Second", () => Task.FromResult(true));
        registry.Register("Third", () => Task.FromResult(true));

        Assert.Equal("First", registry.Steps[0].Name);
        Assert.Equal("Second", registry.Steps[1].Name);
        Assert.Equal("Third", registry.Steps[2].Name);
    }

    [Fact]
    public void Steps_IsReadOnlyCollection()
    {
        var registry = new StepRegistry();
        registry.Register("Test", () => Task.FromResult(true));

        Assert.IsAssignableFrom<IReadOnlyList<BuildStep>>(registry.Steps);
    }

    [Fact]
    public void Register_MultipleSteps_MaintainsCount()
    {
        var registry = new StepRegistry();

        for (int i = 0; i < 10; i++)
        {
            registry.Register($"Step{i}", () => Task.FromResult(true));
        }

        Assert.Equal(10, registry.Steps.Count);
    }
}
