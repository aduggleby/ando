// =============================================================================
// WorkflowResultTests.cs
//
// Summary: Unit tests for WorkflowResult and StepResult classes.
//
// Tests verify that:
// - Properties have correct default values
// - Computed properties (StepsRun, StepsFailed) calculate correctly
// - Init-only properties can be set during construction
// =============================================================================

using Ando.Workflow;

namespace Ando.Tests.Unit.Workflow;

[Trait("Category", "Unit")]
public class WorkflowResultTests
{
    [Fact]
    public void WorkflowResult_HasDefaultValues()
    {
        var result = new WorkflowResult();

        Assert.Equal("", result.WorkflowName);
        Assert.False(result.Success);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Empty(result.StepResults);
    }

    [Fact]
    public void WorkflowResult_InitProperties_CanBeSet()
    {
        var result = new WorkflowResult
        {
            WorkflowName = "test",
            Success = true,
            Duration = TimeSpan.FromSeconds(10)
        };

        Assert.Equal("test", result.WorkflowName);
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromSeconds(10), result.Duration);
    }

    [Fact]
    public void StepsRun_ReturnsCountOfStepResults()
    {
        var result = new WorkflowResult
        {
            StepResults =
            [
                new StepResult { StepName = "Step1", Success = true },
                new StepResult { StepName = "Step2", Success = true },
                new StepResult { StepName = "Step3", Success = false }
            ]
        };

        Assert.Equal(3, result.StepsRun);
    }

    [Fact]
    public void StepsFailed_ReturnsCountOfFailedSteps()
    {
        var result = new WorkflowResult
        {
            StepResults =
            [
                new StepResult { StepName = "Step1", Success = true },
                new StepResult { StepName = "Step2", Success = false },
                new StepResult { StepName = "Step3", Success = false },
                new StepResult { StepName = "Step4", Success = true }
            ]
        };

        Assert.Equal(2, result.StepsFailed);
    }

    [Fact]
    public void StepsFailed_ReturnsZero_WhenAllSucceed()
    {
        var result = new WorkflowResult
        {
            StepResults =
            [
                new StepResult { StepName = "Step1", Success = true },
                new StepResult { StepName = "Step2", Success = true }
            ]
        };

        Assert.Equal(0, result.StepsFailed);
    }

    [Fact]
    public void StepsRun_ReturnsZero_WhenNoSteps()
    {
        var result = new WorkflowResult();

        Assert.Equal(0, result.StepsRun);
    }
}

[Trait("Category", "Unit")]
public class StepResultTests
{
    [Fact]
    public void StepResult_HasDefaultValues()
    {
        var result = new StepResult();

        Assert.Equal("", result.StepName);
        Assert.Null(result.Context);
        Assert.False(result.Success);
        Assert.Equal(TimeSpan.Zero, result.Duration);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void StepResult_InitProperties_CanBeSet()
    {
        var result = new StepResult
        {
            StepName = "Dotnet.Build",
            Context = "MyProject",
            Success = true,
            Duration = TimeSpan.FromMilliseconds(500),
            ErrorMessage = null
        };

        Assert.Equal("Dotnet.Build", result.StepName);
        Assert.Equal("MyProject", result.Context);
        Assert.True(result.Success);
        Assert.Equal(TimeSpan.FromMilliseconds(500), result.Duration);
        Assert.Null(result.ErrorMessage);
    }

    [Fact]
    public void StepResult_FailedStep_HasErrorMessage()
    {
        var result = new StepResult
        {
            StepName = "Dotnet.Test",
            Success = false,
            ErrorMessage = "Tests failed: 3 of 10"
        };

        Assert.False(result.Success);
        Assert.Equal("Tests failed: 3 of 10", result.ErrorMessage);
    }
}
