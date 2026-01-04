// =============================================================================
// FunctionsOperationsTests.cs
//
// Summary: Unit tests for FunctionsOperations class.
//
// Tests verify that:
// - DeployZip registers correct steps with proper arguments
// - Publish uses Azure Functions Core Tools command
// - Deployment slot options are correctly translated to CLI flags
// - SwapSlots generates correct swap command
// - Start/Stop/Restart commands work correctly
//
// Design: Uses MockExecutor to capture commands without execution,
// and TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class FunctionsOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private FunctionsOperations CreateFunctions() =>
        new FunctionsOperations(_registry, _logger, () => _executor);

    [Fact]
    public void DeployZip_RegistersStep()
    {
        var functions = CreateFunctions();

        functions.DeployZip("my-func", "./publish.zip");

        Assert.Single(_registry.Steps);
        Assert.Equal("Functions.DeployZip", _registry.Steps[0].Name);
        Assert.Equal("my-func", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task DeployZip_ExecutesCorrectCommand()
    {
        var functions = CreateFunctions();
        functions.DeployZip("my-func", "./publish.zip");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("functionapp", cmd.Args);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("source", cmd.Args);
        Assert.Contains("config-zip", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-func", cmd.GetArgValue("--name"));
        Assert.Contains("--src", cmd.Args);
        Assert.Equal("./publish.zip", cmd.GetArgValue("--src"));
    }

    [Fact]
    public async Task DeployZip_WithResourceGroup_IncludesResourceGroup()
    {
        var functions = CreateFunctions();
        functions.DeployZip("my-func", "./publish.zip", "my-rg");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--resource-group", cmd.Args);
        Assert.Equal("my-rg", cmd.GetArgValue("--resource-group"));
    }

    [Fact]
    public async Task DeployZip_WithDeploymentSlot_IncludesSlot()
    {
        var functions = CreateFunctions();
        functions.DeployZip("my-func", "./publish.zip", configure: o => o.WithDeploymentSlot("staging"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
    }

    [Fact]
    public async Task DeployZip_WithNoWait_IncludesAsyncFlag()
    {
        var functions = CreateFunctions();
        functions.DeployZip("my-func", "./publish.zip", configure: o => o.WithNoWait());

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--async", cmd.Args);
    }

    [Fact]
    public void Publish_RegistersStep()
    {
        var functions = CreateFunctions();

        functions.Publish("my-func");

        Assert.Single(_registry.Steps);
        Assert.Equal("Functions.Publish", _registry.Steps[0].Name);
        Assert.Equal("my-func", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task Publish_ExecutesCorrectCommand()
    {
        var functions = CreateFunctions();
        functions.Publish("my-func");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("func", cmd.Command);
        Assert.Contains("azure", cmd.Args);
        Assert.Contains("functionapp", cmd.Args);
        Assert.Contains("publish", cmd.Args);
        Assert.Contains("my-func", cmd.Args);
    }

    [Fact]
    public async Task Publish_WithDeploymentSlot_IncludesSlot()
    {
        var functions = CreateFunctions();
        functions.Publish("my-func", configure: o => o.WithDeploymentSlot("staging"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
    }

    [Fact]
    public async Task Publish_WithConfiguration_IncludesConfiguration()
    {
        var functions = CreateFunctions();
        functions.Publish("my-func", configure: o => o.WithConfiguration("Release"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--configuration", cmd.Args);
        Assert.Equal("Release", cmd.GetArgValue("--configuration"));
    }

    [Fact]
    public async Task Publish_WithForceRestart_IncludesForceFlag()
    {
        var functions = CreateFunctions();
        functions.Publish("my-func", configure: o => o.WithForceRestart());

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--force", cmd.Args);
    }

    [Fact]
    public void SwapSlots_RegistersStep()
    {
        var functions = CreateFunctions();

        functions.SwapSlots("my-func", "staging");

        Assert.Single(_registry.Steps);
        Assert.Equal("Functions.SwapSlots", _registry.Steps[0].Name);
        Assert.Equal("staging -> production", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task SwapSlots_ExecutesCorrectCommand()
    {
        var functions = CreateFunctions();
        functions.SwapSlots("my-func", "staging");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("functionapp", cmd.Args);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("slot", cmd.Args);
        Assert.Contains("swap", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-func", cmd.GetArgValue("--name"));
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
        Assert.Contains("--target-slot", cmd.Args);
        Assert.Equal("production", cmd.GetArgValue("--target-slot"));
    }

    [Fact]
    public async Task SwapSlots_WithCustomTargetSlot_IncludesTargetSlot()
    {
        var functions = CreateFunctions();
        functions.SwapSlots("my-func", "staging", targetSlot: "preview");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("preview", cmd.GetArgValue("--target-slot"));
    }

    [Fact]
    public void DeployWithSwap_RegistersTwoSteps()
    {
        var functions = CreateFunctions();

        functions.DeployWithSwap("my-func", "./publish.zip");

        Assert.Equal(2, _registry.Steps.Count);
        Assert.Equal("Functions.DeployZip", _registry.Steps[0].Name);
        Assert.Equal("Functions.SwapSlots", _registry.Steps[1].Name);
    }

    [Fact]
    public void Restart_RegistersStep()
    {
        var functions = CreateFunctions();

        functions.Restart("my-func");

        Assert.Single(_registry.Steps);
        Assert.Equal("Functions.Restart", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Restart_ExecutesCorrectCommand()
    {
        var functions = CreateFunctions();
        functions.Restart("my-func");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("functionapp", cmd.Args);
        Assert.Contains("restart", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-func", cmd.GetArgValue("--name"));
    }

    [Fact]
    public async Task Restart_WithSlot_IncludesSlot()
    {
        var functions = CreateFunctions();
        functions.Restart("my-func", slot: "staging");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
    }

    [Fact]
    public void Stop_RegistersStep()
    {
        var functions = CreateFunctions();

        functions.Stop("my-func");

        Assert.Single(_registry.Steps);
        Assert.Equal("Functions.Stop", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Stop_ExecutesCorrectCommand()
    {
        var functions = CreateFunctions();
        functions.Stop("my-func", "my-rg");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("functionapp", cmd.Args);
        Assert.Contains("stop", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-func", cmd.GetArgValue("--name"));
        Assert.Contains("--resource-group", cmd.Args);
        Assert.Equal("my-rg", cmd.GetArgValue("--resource-group"));
    }

    [Fact]
    public void Start_RegistersStep()
    {
        var functions = CreateFunctions();

        functions.Start("my-func");

        Assert.Single(_registry.Steps);
        Assert.Equal("Functions.Start", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Start_ExecutesCorrectCommand()
    {
        var functions = CreateFunctions();
        functions.Start("my-func");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("functionapp", cmd.Args);
        Assert.Contains("start", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-func", cmd.GetArgValue("--name"));
    }

    [Fact]
    public void Show_RegistersStep()
    {
        var functions = CreateFunctions();

        functions.Show("my-func");

        Assert.Single(_registry.Steps);
        Assert.Equal("Functions.Show", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Show_ExecutesCorrectCommand()
    {
        var functions = CreateFunctions();
        functions.Show("my-func", "my-rg");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("functionapp", cmd.Args);
        Assert.Contains("show", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-func", cmd.GetArgValue("--name"));
        Assert.Contains("--resource-group", cmd.Args);
        Assert.Equal("my-rg", cmd.GetArgValue("--resource-group"));
    }

    [Fact]
    public async Task AllOperations_ReturnFalse_WhenCommandFails()
    {
        var functions = CreateFunctions();
        _executor.SimulateFailure = true;

        functions.DeployZip("my-func", "./publish.zip");
        var success = await _registry.Steps[0].Execute();

        Assert.False(success);
    }

    [Fact]
    public async Task FluentOptions_CanBeChained()
    {
        var functions = CreateFunctions();
        functions.DeployZip("my-func", "./publish.zip", "my-rg", o => o
            .WithDeploymentSlot("staging")
            .WithNoWait());

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
        Assert.Contains("--async", cmd.Args);
    }
}
