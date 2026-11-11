// =============================================================================
// FunctionsParityTests.cs
//
// Summary: Tests for the Functions operations that mirror AppService: zip deploy
// options (restart default false, timeout, slot), CommandStep-returning reads
// (Show, GetHostName, AccessRestrictions), lifecycle steps, slot management and
// TemporaryAccessRule, all against `az functionapp`.
// =============================================================================

using Ando.Execution;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class FunctionsParityTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private FunctionsOperations CreateFunctions() =>
        new(_registry, _logger, () => _executor, _ => Task.CompletedTask);

    private Task<WorkflowResult> RunAsync() => new WorkflowRunner(_registry, _logger).RunAsync(new BuildOptions());

    // ---- DeployZip ----------------------------------------------------------

    [Fact]
    public async Task DeployZip_DefaultsToConfigZip()
    {
        CreateFunctions().DeployZip("func", "./publish.zip", "rg", o => o.WithDeploymentSlot("staging"));

        await RunAsync();

        _executor.LastCommand!.CommandLine.ShouldBe(
            "az functionapp deployment source config-zip --name func --resource-group rg --src ./publish.zip --slot staging --output none");
    }

    [Fact]
    public async Task DeployZip_OneDeploy_UsesFunctionappDeployWithRestartFalseByDefault()
    {
        CreateFunctions().DeployZip("func", "./publish.zip", "rg", o => o.WithOneDeploy());

        await RunAsync();

        _executor.LastCommand!.CommandLine.ShouldBe(
            "az functionapp deploy --name func --resource-group rg --src-path ./publish.zip --type zip --restart false --async false --output none");
    }

    [Fact]
    public async Task DeployZip_WithRestartSlotAndNoWait_PassesFlags()
    {
        CreateFunctions().DeployZip("func", "./publish.zip", configure: o => o.WithOneDeploy().WithDeploymentSlot("staging").WithRestart().WithNoWait());

        await RunAsync();

        var cmd = _executor.LastCommand!;
        cmd.GetArgValue("--slot").ShouldBe("staging");
        cmd.GetArgValue("--restart").ShouldBe("true");
        cmd.GetArgValue("--async").ShouldBe("true");
    }

    [Fact]
    public async Task DeployZip_OneDeploy_WithTimeout_PassesAzTimeoutInMilliseconds()
    {
        CreateFunctions().DeployZip("func", "./publish.zip", configure: o => o.WithOneDeploy().WithTimeout(TimeSpan.FromMinutes(30)));

        await RunAsync();

        var azTimeout = int.Parse(_executor.LastCommand!.GetArgValue("--timeout")!);
        azTimeout.ShouldBeInRange(30 * 60 * 1000 - 20_000, 30 * 60 * 1000 - 10_000 + 1);
        _executor.LastCommand!.Options!.TimeoutMs.ShouldBeLessThanOrEqualTo(30 * 60 * 1000);
    }

    [Fact]
    public async Task DeployZip_ConfigZip_WithTimeout_PassesAzTimeoutInSeconds()
    {
        CreateFunctions().DeployZip("func", "./publish.zip", configure: o => o.WithTimeout(TimeSpan.FromMinutes(30)));

        await RunAsync();

        var azSeconds = int.Parse(_executor.LastCommand!.GetArgValue("--timeout")!);
        azSeconds.ShouldBeInRange(30 * 60 - 20, 30 * 60 - 9);
    }

    [Fact]
    public void DeployZip_RestartOrNoWaitWithoutOneDeploy_Throws()
    {
        var restart = Should.Throw<InvalidOperationException>(() => CreateFunctions().DeployZip("func", "z.zip", configure: o => o.WithRestart()));
        restart.Message.ShouldContain("WithOneDeploy()");
        Should.Throw<InvalidOperationException>(() => CreateFunctions().DeployZip("func", "z.zip", configure: o => o.WithNoWait()));
        _registry.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task DeployZip_ConfigZip_TransientFailureRetries_TimeoutIsNotReconciled()
    {
        _executor.QueuedResults.Enqueue(CommandResult.Failed(1, "503 Service Unavailable"));
        CreateFunctions().DeployZip("func", "./publish.zip");
        (await RunAsync()).Success.ShouldBeTrue();
        _executor.ExecutedCommands.Count.ShouldBe(2);

        _executor.ExecutedCommands.Clear();
        _executor.QueuedResults.Enqueue(CommandResult.Failed(-1, "Command timed out after 1200000ms"));
        var registry = new StepRegistry();
        new FunctionsOperations(registry, _logger, () => _executor, _ => Task.CompletedTask).DeployZip("func", "./publish.zip");
        (await registry.Steps[0].Execute()).ShouldBeFalse();
        _executor.ExecutedCommands.ShouldHaveSingleItem();
    }

    [Fact]
    public async Task DeployZip_DefaultTimeout_IsTwentyMinutesWithoutAzTimeout()
    {
        CreateFunctions().DeployZip("func", "./publish.zip");

        await RunAsync();

        _executor.LastCommand!.Args.ShouldNotContain("--timeout");
        _executor.LastCommand!.Options!.TimeoutMs.ShouldBeInRange(20 * 60 * 1000 - 5_000, 20 * 60 * 1000);
    }

    [Fact]
    public void WithTimeout_NonPositive_Throws()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new FunctionsDeployOptions().WithTimeout(TimeSpan.Zero));
    }

    [Fact]
    public async Task DeployZip_TransientFailure_Retries()
    {
        _executor.QueuedResults.Enqueue(CommandResult.Failed(1, "503 Service Unavailable"));
        CreateFunctions().DeployZip("func", "./publish.zip");

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _executor.ExecutedCommands.Count.ShouldBe(2);
        _executor.ExecutedCommands.All(c => c.CommandLine.Contains("functionapp deploy")).ShouldBeTrue();
    }

    [Fact]
    public async Task DeployZip_TimeoutWithSuccessfulDeploymentStatus_ReturnsTrue()
    {
        _executor.QueuedResults.Enqueue(CommandResult.Failed(-1, "Command timed out after 1200000ms"));
        _executor.QueuedResults.Enqueue(CommandResult.Ok("""{"status": 4, "message": "Deployment successful", "end_time": "2099-01-01T00:00:00Z"}"""));
        CreateFunctions().DeployZip("func", "./publish.zip", "rg", o => o.WithOneDeploy().WithDeploymentSlot("staging"));

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _executor.ExecutedCommands[1].CommandLine.ShouldStartWith("az functionapp log deployment show");
        _executor.ExecutedCommands[1].GetArgValue("--slot").ShouldBe("staging");
    }

    [Fact]
    public void DeployWithSwap_DeploysToSlotWithoutRestartThenSwaps()
    {
        CreateFunctions().DeployWithSwap("func", "./publish.zip", "release", "rg");

        _registry.Steps.Select(s => s.Name).ToArray().ShouldBe(["Functions.DeployZip", "Functions.SwapSlots"]);
    }

    // ---- Lifecycle and reads ------------------------------------------------

    [Fact]
    public async Task SwapSlots_ReturnsCommandStepWithThirtyMinuteDefault()
    {
        var swap = CreateFunctions().SwapSlots("func", "staging", "rg");

        await RunAsync();

        swap.TimeoutMs.ShouldBe(30 * 60 * 1000);
        _executor.LastCommand!.CommandLine.ShouldBe(
            "az functionapp deployment slot swap --name func --resource-group rg --slot staging --target-slot production --output none");
    }

    [Theory]
    [InlineData("Restart", "restart")]
    [InlineData("Stop", "stop")]
    [InlineData("Start", "start")]
    public async Task Lifecycle_ReturnsCommandStepAndBuildsSlotArgs(string operation, string action)
    {
        var functions = CreateFunctions();
        CommandStep step = operation switch
        {
            "Restart" => functions.Restart("func", "rg", "staging"),
            "Stop" => functions.Stop("func", "rg", "staging"),
            _ => functions.Start("func", "rg", "staging")
        };

        await RunAsync();

        step.ShouldNotBeNull();
        _registry.Steps[0].Name.ShouldBe($"Functions.{operation}");
        _executor.LastCommand!.CommandLine.ShouldBe(
            $"az functionapp {action} --name func --resource-group rg --slot staging --output none");
    }

    [Fact]
    public async Task Show_RunsFunctionappShowAsQuietJsonWithSlot()
    {
        _executor.SimulatedOutput = """{"state":"Running"}""";

        var show = CreateFunctions().Show("func", "rg", "release");
        await RunAsync();

        _registry.Steps[0].Name.ShouldBe("Functions.Show");
        var command = _executor.ExecutedCommands.ShouldHaveSingleItem();
        command.CommandLine.ShouldBe("az functionapp show --name func --resource-group rg --slot release --output json");
        command.Options!.SuppressOutput.ShouldBeTrue();
        show.Json("state").ShouldBe("Running");
    }

    [Fact]
    public async Task Show_WithoutResourceGroupOrSlot_OmitsThoseFlags()
    {
        CreateFunctions().Show("func");

        await RunAsync();

        _executor.LastCommand!.CommandLine.ShouldBe("az functionapp show --name func --output json");
    }

    [Fact]
    public async Task GetHostName_QueriesDefaultHostNameAsTsv()
    {
        _executor.SimulatedOutput = "func-release.azurewebsites.net\n";

        var host = CreateFunctions().GetHostName("func", "rg", "release");
        await RunAsync();

        _registry.Steps[0].Name.ShouldBe("Functions.GetHostName");
        _executor.LastCommand!.CommandLine.ShouldBe(
            "az functionapp show --name func --resource-group rg --slot release --query defaultHostName --output tsv");
        host.Value.ShouldBe("func-release.azurewebsites.net");
    }

    [Fact]
    public async Task AccessRestrictions_ShowsRulesAsQuietJson()
    {
        _executor.SimulatedOutput = """{"ipSecurityRestrictionsDefaultAction":"Deny"}""";

        var rules = CreateFunctions().AccessRestrictions("func", "rg", "release");
        await RunAsync();

        _registry.Steps[0].Name.ShouldBe("Functions.AccessRestrictions");
        var command = _executor.ExecutedCommands.ShouldHaveSingleItem();
        command.CommandLine.ShouldBe(
            "az functionapp config access-restriction show --name func --resource-group rg --slot release --output json");
        command.Options!.SuppressOutput.ShouldBeTrue();
        rules.Json("ipSecurityRestrictionsDefaultAction").ShouldBe("Deny");
    }

    // ---- Slots --------------------------------------------------------------

    [Fact]
    public async Task ListCreateDeleteSlot_BuildFunctionappSlotCommands()
    {
        var functions = CreateFunctions();
        functions.ListSlots("func", "rg");
        functions.CreateSlot("func", "release", "rg", "production");
        functions.DeleteSlot("func", "release", "rg");

        await RunAsync();

        _registry.Steps.Select(s => s.Name).ToArray().ShouldBe(
            ["Functions.ListSlots", "Functions.CreateSlot", "Functions.DeleteSlot"]);
        _executor.ExecutedCommands.Select(c => c.CommandLine).ToArray().ShouldBe([
            "az functionapp deployment slot list --name func --resource-group rg",
            "az functionapp deployment slot create --name func --slot release --resource-group rg --configuration-source production --output none",
            "az functionapp deployment slot delete --name func --slot release --resource-group rg --output none"
        ]);
    }

    // ---- TemporaryAccessRule -------------------------------------------------

    [Fact]
    public async Task TemporaryAccessRule_AddsRuleThenRemovesAndVerifiesWithFunctionappGroup()
    {
        CreateFunctions().TemporaryAccessRule("func", "rg", "release", "gate", "203.0.113.7", 60);

        _registry.Steps.ShouldHaveSingleItem().Name.ShouldBe("Functions.TemporaryAccessRule");
        _registry.Root.Finally.ShouldHaveSingleItem();

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        var commands = _executor.ExecutedCommands.Select(c => c.CommandLine).ToList();
        commands.Count.ShouldBe(4);
        commands[0].ShouldBe("az functionapp config access-restriction remove --name func --resource-group rg --slot release --rule-name gate --output none");
        commands[1].ShouldBe("az functionapp config access-restriction add --name func --resource-group rg --slot release --rule-name gate --priority 60 --action Allow --ip-address 203.0.113.7/32 --output none");
        commands[2].ShouldBe("az functionapp config access-restriction remove --name func --resource-group rg --slot release --rule-name gate --output none");
        commands[3].ShouldStartWith("az functionapp config access-restriction show --name func --resource-group rg --slot release");
        _registry.AllSteps.Select(s => s.Name).ShouldContain("Functions.RemoveAccessRule");
    }
}
