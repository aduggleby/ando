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
using Ando.Execution;
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
        new AppServiceOperations(_registry, _logger, () => _executor, _ => Task.CompletedTask);

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
        cmd.Args.Take(2).ShouldBe(["webapp", "deploy"]);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-app", cmd.GetArgValue("--name"));
        Assert.Equal("./publish.zip", cmd.GetArgValue("--src-path"));
        cmd.GetArgValue("--type").ShouldBe("zip");
        cmd.HasArg("--restart").ShouldBeFalse();
        cmd.GetArgValue("--async").ShouldBe("false");
        cmd.Args.ShouldNotContain("--timeout");
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

        _executor.LastCommand!.GetArgValue("--async").ShouldBe("true");
    }

    [Fact]
    public async Task DeployZip_UsesDefaultLongTimeout()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip");

        await _registry.Steps[0].Execute();

        _executor.LastCommand!.Options.ShouldNotBeNull();
        // The remaining budget is passed, so a few milliseconds may already be used.
        _executor.LastCommand!.Options!.TimeoutMs.ShouldBeInRange(20 * 60 * 1000 - 5_000, 20 * 60 * 1000);
    }

    [Fact]
    public async Task DeployZip_WithTimeout_UsesConfiguredTimeout()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip", configure: o => o.WithTimeout(TimeSpan.FromMinutes(30)));

        await _registry.Steps[0].Execute();

        _executor.LastCommand!.Options.ShouldNotBeNull();
        _executor.LastCommand!.Options!.TimeoutMs.ShouldBe((int)TimeSpan.FromMinutes(30).TotalMilliseconds);
    }

    [Fact]
    public async Task DeployZip_RetriesTransientFailure_ThenSucceeds()
    {
        var appService = CreateAppService();
        _executor.QueuedResults.Enqueue(CommandResult.Failed(1, "SCM returned 503 Service Unavailable"));
        _executor.QueuedResults.Enqueue(CommandResult.Ok());

        appService.DeployZip("my-app", "./publish.zip");

        var success = await _registry.Steps[0].Execute();

        success.ShouldBeTrue();
        _executor.ExecutedCommands.Count.ShouldBe(2);
        _executor.ExecutedCommands.All(command => command.CommandLine.Contains("webapp deploy")).ShouldBeTrue();
    }

    [Fact]
    public async Task DeployZip_TimeoutWithSuccessfulDeploymentStatus_ReturnsTrue()
    {
        var appService = CreateAppService();
        _executor.QueuedResults.Enqueue(CommandResult.Failed(-1, "Command timed out after 1200000ms"));
        _executor.QueuedResults.Enqueue(CommandResult.Ok("""
            {
              "status": 4,
              "message": "Deployment successful",
              "end_time": "2099-01-01T00:00:00Z"
            }
            """));

        appService.DeployZip("my-app", "./publish.zip", "my-rg", options => options.WithDeploymentSlot("staging"));

        var success = await _registry.Steps[0].Execute();

        success.ShouldBeTrue();
        _executor.ExecutedCommands.Count.ShouldBe(2);
        _executor.ExecutedCommands[0].CommandLine.ShouldContain("webapp deploy");
        _executor.ExecutedCommands[1].CommandLine.ShouldContain("log deployment show");
        _executor.ExecutedCommands[1].GetArgValue("--resource-group").ShouldBe("my-rg");
        _executor.ExecutedCommands[1].GetArgValue("--slot").ShouldBe("staging");
    }

    [Fact]
    public async Task DeployZip_PersistentFailure_ReturnsFalse()
    {
        var appService = CreateAppService();
        _executor.QueuedResults.Enqueue(CommandResult.Failed(1, "zip package was not found"));

        appService.DeployZip("my-app", "./publish.zip");

        var success = await _registry.Steps[0].Execute();

        success.ShouldBeFalse();
        _executor.ExecutedCommands.ShouldHaveSingleItem();
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

        var cmd = _executor.ExecutedCommands[0];
        Assert.Contains("--slot", cmd.Args);
        Assert.Equal("staging", cmd.GetArgValue("--slot"));
        cmd.GetArgValue("--async").ShouldBe("true");
        cmd.GetArgValue("--restart").ShouldBe("true");
    }

    [Fact]
    public async Task DeployZip_WithTimeout_PassesAzTimeoutInMilliseconds()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip", configure: o => o.WithTimeout(TimeSpan.FromMinutes(30)));

        await _registry.Steps[0].Execute();

        var azTimeout = int.Parse(_executor.LastCommand!.GetArgValue("--timeout")!);
        azTimeout.ShouldBeInRange(30 * 60 * 1000 - 20_000, 30 * 60 * 1000 - 10_000 + 1);
    }

    [Fact]
    public async Task DeployZip_WithRestart_RestartsViaAzDeployInOneCommand()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip", "my-rg", o => o.WithDeploymentSlot("staging").WithRestart());

        (await _registry.Steps[0].Execute()).ShouldBeTrue();

        var deploy = _executor.ExecutedCommands.ShouldHaveSingleItem();
        deploy.Args.Take(2).ShouldBe(["webapp", "deploy"]);
        deploy.GetArgValue("--restart").ShouldBe("true");
        deploy.GetArgValue("--resource-group").ShouldBe("my-rg");
        deploy.GetArgValue("--slot").ShouldBe("staging");
    }

    [Fact]
    public async Task DeployZip_WithoutRestart_DoesNotRestart()
    {
        var appService = CreateAppService();
        appService.DeployZip("my-app", "./publish.zip");

        await _registry.Steps[0].Execute();

        _executor.ExecutedCommands.ShouldHaveSingleItem().HasArg("--restart").ShouldBeFalse();
    }

    [Fact]
    public void SwapSlots_ReturnsCommandStepWithThirtyMinuteDefaultTimeout()
    {
        var swap = CreateAppService().SwapSlots("my-app", "staging");

        swap.ShouldBeOfType<CommandStep>();
        swap.TimeoutMs.ShouldBe(30 * 60 * 1000);
    }

    [Fact]
    public void SwapSlots_LongerCommandTimeout_Wins_ExplicitWithTimeoutOverrides()
    {
        _registry.DefaultCommandTimeoutMs = 45 * 60 * 1000;
        var appService = CreateAppService();

        appService.SwapSlots("my-app", "staging").TimeoutMs.ShouldBe(45 * 60 * 1000);
        appService.SwapSlots("my-app", "staging").WithTimeout(TimeSpan.FromMinutes(2)).TimeoutMs.ShouldBe(2 * 60 * 1000);
    }

    [Fact]
    public void SwapSlots_ShorterCommandTimeout_StillThirtyMinutes()
    {
        _registry.DefaultCommandTimeoutMs = 10 * 60 * 1000;

        CreateAppService().SwapSlots("my-app", "staging").TimeoutMs.ShouldBe(30 * 60 * 1000);
    }

    [Fact]
    public async Task SwapSlots_WithTimeout_PassesTimeoutToCommand()
    {
        CreateAppService().SwapSlots("my-app", "staging").WithTimeout(TimeSpan.FromMinutes(7));

        await _registry.Steps[0].Execute();

        _executor.LastCommand!.Options!.TimeoutMs.ShouldBe(7 * 60 * 1000);
    }

    [Fact]
    public void StopStartRestart_ReturnCommandSteps()
    {
        var appService = CreateAppService();

        appService.Stop("a").WithTimeout(TimeSpan.FromMinutes(1)).TimeoutMs.ShouldBe(60_000);
        appService.Start("a").ShouldBeOfType<CommandStep>();
        appService.Restart("a").ShouldBeOfType<CommandStep>();
        appService.Restart("a").TimeoutMs.ShouldBe(CommandOptions.DefaultTimeoutMs);
    }

    [Fact]
    public void DeployFolder_RegistersZipThenDeploySteps()
    {
        var appService = CreateAppService();

        appService.DeployFolder("my-app", "dist/api");

        Assert.Equal(2, _registry.Steps.Count);
        Assert.Equal("AppService.ZipFolder", _registry.Steps[0].Name);
        Assert.Equal("AppService.DeployZip", _registry.Steps[1].Name);
    }

    [Fact]
    public async Task DeployFolder_ZipStep_ArchivesFolderContentsAtRoot()
    {
        var appService = CreateAppService();
        appService.DeployFolder("my-app", "dist/api");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("sh", cmd.Command);
        // cd into the folder and zip "." so the archive has no nested top-level dir.
        var script = string.Join(" ", cmd.Args);
        Assert.Contains("cd 'dist/api'", script);
        Assert.Contains("zip -rq 'dist/api.zip' .", script);
    }

    [Fact]
    public async Task DeployFolder_DeployStep_UsesSiblingZipPath()
    {
        var appService = CreateAppService();
        appService.DeployFolder("my-app", "dist/api", "my-rg");

        await _registry.Steps[1].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Equal("dist/api.zip", cmd.GetArgValue("--src-path"));
        Assert.Equal("my-rg", cmd.GetArgValue("--resource-group"));
    }

    [Fact]
    public async Task DeployFolder_WithSlot_PassesSlotToDeploy()
    {
        var appService = CreateAppService();
        appService.DeployFolder("my-app", "dist/api", configure: o => o.WithDeploymentSlot("release"));

        await _registry.Steps[1].Execute();

        Assert.Equal("release", _executor.LastCommand!.GetArgValue("--slot"));
    }
}
