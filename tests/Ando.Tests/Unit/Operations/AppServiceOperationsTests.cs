// =============================================================================
// AppServiceOperationsTests.cs
//
// Summary: Unit tests for AppServiceOperations class.
//
// Tests verify that:
// - DeployZip registers correct steps with proper arguments
// - Deployment slot options are correctly translated to CLI flags
// - SwapSlots generates correct swap command
// - Start/Stop/Restart commands work correctly
// - Slot management operations (Create, Delete, List) work correctly
//
// Design: Uses MockExecutor to capture commands without execution,
// and TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class AppServiceOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private AppServiceOperations CreateAppService() =>
        new AppServiceOperations(_registry, _logger, () => _executor);

    [Fact]
    public void DeployZip_RegistersStep()
    {
        var appService = CreateAppService();

        appService.DeployZip("my-app", "./publish.zip");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.DeployZip", _registry.Steps[0].Name);
        Assert.Equal("my-app", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task DeployZip_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("source", cmd.Args);
        Assert.Contains("config-zip", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
        Assert.Contains("--src", cmd.Args);
        Assert.Equal("./publish.zip", cmd.GetArgValue("--src"));
    }

    [Fact]
    public async Task DeployZip_WithResourceGroup_IncludesResourceGroup()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip", "my-rg");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--resource-group", cmd.Args);
        Assert.Equal("my-rg", cmd.GetArgValue("--resource-group"));
    }

    [Fact]
    public async Task DeployZip_WithDeploymentSlot_IncludesSlot()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip", configure: o => o.WithDeploymentSlot("staging"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
    }

    [Fact]
    public async Task DeployZip_WithNoWait_IncludesAsyncFlag()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip", configure: o => o.WithNoWait());

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--async", cmd.Args);
    }

    [Fact]
    public void SwapSlots_RegistersStep()
    {
        var appService = CreateAppService();

        appService.SwapSlots("my-app", "staging");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.SwapSlots", _registry.Steps[0].Name);
        Assert.Equal("staging -> production", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task SwapSlots_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.SwapSlots("my-app", "staging");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("slot", cmd.Args);
        Assert.Contains("swap", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
        Assert.Contains("--target-slot", cmd.Args);
        Assert.Equal("production", cmd.GetArgValue("--target-slot"));
    }

    [Fact]
    public async Task SwapSlots_WithCustomTargetSlot_IncludesTargetSlot()
    {
        var appService = CreateAppService();
        appService.SwapSlots("my-app", "staging", targetSlot: "preview");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("preview", cmd.GetArgValue("--target-slot"));
    }

    [Fact]
    public async Task SwapSlots_WithResourceGroup_IncludesResourceGroup()
    {
        var appService = CreateAppService();
        appService.SwapSlots("my-app", "staging", "my-rg");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--resource-group", cmd.Args);
        Assert.Equal("my-rg", cmd.GetArgValue("--resource-group"));
    }

    [Fact]
    public void DeployWithSwap_RegistersTwoSteps()
    {
        var appService = CreateAppService();

        appService.DeployWithSwap("my-app", "./publish.zip");

        Assert.Equal(2, _registry.Steps.Count);
        Assert.Equal("AppService.DeployZip", _registry.Steps[0].Name);
        Assert.Equal("AppService.SwapSlots", _registry.Steps[1].Name);
    }

    [Fact]
    public void Restart_RegistersStep()
    {
        var appService = CreateAppService();

        appService.Restart("my-app");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.Restart", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Restart_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.Restart("my-app");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("restart", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
    }

    [Fact]
    public async Task Restart_WithSlot_IncludesSlot()
    {
        var appService = CreateAppService();
        appService.Restart("my-app", slot: "staging");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
    }

    [Fact]
    public void Stop_RegistersStep()
    {
        var appService = CreateAppService();

        appService.Stop("my-app");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.Stop", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Stop_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.Stop("my-app", "my-rg");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("stop", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
        Assert.Contains("--resource-group", cmd.Args);
        Assert.Equal("my-rg", cmd.GetArgValue("--resource-group"));
    }

    [Fact]
    public void Start_RegistersStep()
    {
        var appService = CreateAppService();

        appService.Start("my-app");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.Start", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Start_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.Start("my-app");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("start", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
    }

    [Fact]
    public void Show_RegistersStep()
    {
        var appService = CreateAppService();

        appService.Show("my-app");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.Show", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Show_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.Show("my-app", "my-rg");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("show", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
        Assert.Contains("--resource-group", cmd.Args);
        Assert.Equal("my-rg", cmd.GetArgValue("--resource-group"));
    }

    [Fact]
    public void ListSlots_RegistersStep()
    {
        var appService = CreateAppService();

        appService.ListSlots("my-app");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.ListSlots", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task ListSlots_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.ListSlots("my-app");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("slot", cmd.Args);
        Assert.Contains("list", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
    }

    [Fact]
    public void CreateSlot_RegistersStep()
    {
        var appService = CreateAppService();

        appService.CreateSlot("my-app", "staging");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.CreateSlot", _registry.Steps[0].Name);
        Assert.Equal("staging", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task CreateSlot_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.CreateSlot("my-app", "staging");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("slot", cmd.Args);
        Assert.Contains("create", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
    }

    [Fact]
    public async Task CreateSlot_WithConfigurationSource_IncludesConfigurationSource()
    {
        var appService = CreateAppService();
        appService.CreateSlot("my-app", "staging", configurationSource: "production");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--configuration-source", cmd.Args);
        Assert.Equal("production", cmd.GetArgValue("--configuration-source"));
    }

    [Fact]
    public void DeleteSlot_RegistersStep()
    {
        var appService = CreateAppService();

        appService.DeleteSlot("my-app", "staging");

        Assert.Single(_registry.Steps);
        Assert.Equal("AppService.DeleteSlot", _registry.Steps[0].Name);
        Assert.Equal("staging", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task DeleteSlot_ExecutesCorrectCommand()
    {
        var appService = CreateAppService();
        appService.DeleteSlot("my-app", "staging");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("webapp", cmd.Args);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("slot", cmd.Args);
        Assert.Contains("delete", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
    }

    [Fact]
    public async Task AllOperations_ReturnFalse_WhenCommandFails()
    {
        var appService = CreateAppService();
        _executor.SimulateFailure = true;

        appService.DeployZip("my-app", "./publish.zip");
        var success = await _registry.Steps[0].Execute();

        Assert.False(success);
    }

    [Fact]
    public async Task FluentOptions_CanBeChained()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip", "my-rg", o => o
            .WithDeploymentSlot("staging")
            .WithNoWait()
            .WithRestart());

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
        Assert.Contains("--async", cmd.Args);
    }
}
