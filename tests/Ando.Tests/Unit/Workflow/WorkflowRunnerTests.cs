// =============================================================================
// WorkflowRunnerTests.cs
//
// Summary: Unit tests for WorkflowRunner class.
//
// Tests verify step execution order, success/failure handling,
// logging behavior, and workflow result generation.
// =============================================================================

using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Workflow;

[Trait("Category", "Unit")]
public class WorkflowRunnerTests
{
    private readonly TestLogger _logger = new();

    [Fact]
    public async Task RunAsync_WithNoSteps_ReturnsSuccess()
    {
        var registry = new StepRegistry();
        var runner = new WorkflowRunner(registry, _logger);
        var options = new BuildOptions();

        var result = await runner.RunAsync(options);

        Assert.True(result.Success);
        Assert.Equal(0, result.StepsRun);
    }

    [Fact]
    public async Task RunAsync_ExecutesAllSteps()
    {
        var registry = new StepRegistry();
        var executionOrder = new List<string>();

        registry.Register("Step1", () =>
        {
            executionOrder.Add("Step1");
            return Task.FromResult(true);
        });
        registry.Register("Step2", () =>
        {
            executionOrder.Add("Step2");
            return Task.FromResult(true);
        });

        var runner = new WorkflowRunner(registry, _logger);
        var options = new BuildOptions();

        var result = await runner.RunAsync(options);

        Assert.True(result.Success);
        Assert.Equal(2, result.StepsRun);
        Assert.Equal(new[] { "Step1", "Step2" }, executionOrder);
    }

    [Fact]
    public async Task RunAsync_StopsOnFailure()
    {
        var registry = new StepRegistry();
        var executionOrder = new List<string>();

        registry.Register("Step1", () =>
        {
            executionOrder.Add("Step1");
            return Task.FromResult(true);
        });
        registry.Register("Step2", () =>
        {
            executionOrder.Add("Step2");
            return Task.FromResult(false);
        });
        registry.Register("Step3", () =>
        {
            executionOrder.Add("Step3");
            return Task.FromResult(true);
        });

        var runner = new WorkflowRunner(registry, _logger);
        var options = new BuildOptions();

        var result = await runner.RunAsync(options);

        Assert.False(result.Success);
        Assert.Equal(2, result.StepsRun);
        Assert.Equal(1, result.StepsFailed);
        Assert.Equal(new[] { "Step1", "Step2" }, executionOrder);
    }

    [Fact]
    public async Task RunAsync_HandlesExceptions()
    {
        var registry = new StepRegistry();

        registry.Register("FailingStep", () =>
        {
            throw new InvalidOperationException("Something went wrong");
        });

        var runner = new WorkflowRunner(registry, _logger);
        var options = new BuildOptions();

        var result = await runner.RunAsync(options);

        Assert.False(result.Success);
        Assert.Equal(1, result.StepsFailed);
        Assert.Contains("Something went wrong", result.StepResults[0].ErrorMessage);
    }

    [Fact]
    public async Task RunAsync_RecordsDurations()
    {
        var registry = new StepRegistry();

        registry.Register("SlowStep", async () =>
        {
            await Task.Delay(50);
            return true;
        });

        var runner = new WorkflowRunner(registry, _logger);
        var options = new BuildOptions();

        var result = await runner.RunAsync(options);

        Assert.True(result.StepResults[0].Duration.TotalMilliseconds >= 40);
        Assert.True(result.Duration.TotalMilliseconds >= 40);
    }

    [Fact]
    public async Task RunAsync_LogsStepEvents()
    {
        var registry = new StepRegistry();
        registry.Register("TestStep", () => Task.FromResult(true));

        var runner = new WorkflowRunner(registry, _logger);
        var options = new BuildOptions();

        await runner.RunAsync(options);

        Assert.Single(_logger.StepsStarted);
        Assert.Single(_logger.StepsCompleted);
        Assert.Equal("TestStep", _logger.StepsStarted[0].StepName);
        Assert.Equal("TestStep", _logger.StepsCompleted[0].StepName);
    }

    [Fact]
    public async Task RunAsync_LogsWorkflowEvents()
    {
        var registry = new StepRegistry();
        var runner = new WorkflowRunner(registry, _logger);
        var options = new BuildOptions();

        await runner.RunAsync(options);

        Assert.Single(_logger.WorkflowsStarted);
        Assert.Single(_logger.WorkflowsCompleted);
        Assert.Equal("build", _logger.WorkflowsStarted[0]);
        Assert.Equal("build", _logger.WorkflowsCompleted[0].WorkflowName);
    }

    [Fact]
    public async Task RunAsync_LogsFailedStep()
    {
        var registry = new StepRegistry();
        registry.Register("FailingStep", () => Task.FromResult(false));

        var runner = new WorkflowRunner(registry, _logger);
        var options = new BuildOptions();

        await runner.RunAsync(options);

        Assert.Single(_logger.StepsFailed);
        Assert.Equal("FailingStep", _logger.StepsFailed[0].StepName);
    }
}
