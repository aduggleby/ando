// =============================================================================
// AppServiceStepModelTests.cs
//
// Summary: Tests for AppService.TemporaryAccessRule (add step plus a finally
// removal step in the current scope) and the CommandStep-returning reads
// Show, GetHostName and AccessRestrictions.
// =============================================================================

using Ando.Operations;
using Ando.Profiles;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class AppServiceStepModelTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();
    private readonly BuildOptions _options = new();

    private AppServiceOperations CreateAppService() =>
        new(_registry, _logger, () => _executor, _ => Task.CompletedTask);

    private AndoOperations CreateAndo() =>
        new(_registry, _logger, p => p, _options, new ArtifactOperations(_logger), new ProfileRegistry());

    private Task<WorkflowResult> RunAsync() => new WorkflowRunner(_registry, _logger).RunAsync(_options);

    // ---- TemporaryAccessRule: registration ---------------------------------

    [Fact]
    public void TemporaryAccessRule_TopLevel_RegistersAddStepAndRootFinally()
    {
        CreateAppService().TemporaryAccessRule("app", "rg", "release", ip: "1.2.3.4");

        var step = _registry.Steps.ShouldHaveSingleItem();
        step.Name.ShouldBe("AppService.TemporaryAccessRule");
        step.Context.ShouldBe("app/release ando-deploy");
        var cleanup = _registry.Root.Finally.ShouldHaveSingleItem();
        cleanup.Name.ShouldBe("Ando.Finally");
        cleanup.ChildScopes.ShouldHaveSingleItem().Steps.ShouldHaveSingleItem().Name.ShouldBe("AppService.RemoveAccessRule");
    }

    [Fact]
    public void TemporaryAccessRule_InsideBlock_LandsInTheBlocksFinally()
    {
        var ando = CreateAndo();
        var appService = CreateAppService();

        ando.Block("eu", () => appService.TemporaryAccessRule("app", "rg", ip: "1.2.3.4"));

        _registry.Root.Finally.ShouldBeEmpty();
        _registry.Steps.ShouldHaveSingleItem().Name.ShouldBe("Ando.Block");
        _registry.AllSteps.Select(s => s.Name).ShouldContain("AppService.TemporaryAccessRule");
        _registry.AllSteps.Select(s => s.Name).ShouldContain("AppService.RemoveAccessRule");
    }

    // ---- TemporaryAccessRule: execution ------------------------------------

    [Fact]
    public async Task TemporaryAccessRule_AddsRuleThenRemovesAndVerifies()
    {
        CreateAppService().TemporaryAccessRule("app", "rg", "release", "gate", "203.0.113.7", 60);

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        var commands = _executor.ExecutedCommands.Select(c => c.CommandLine).ToList();
        commands.Count.ShouldBe(4);
        commands[0].ShouldBe("az webapp config access-restriction remove --name app --resource-group rg --slot release --rule-name gate --output none");
        commands[1].ShouldBe("az webapp config access-restriction add --name app --resource-group rg --slot release --rule-name gate --priority 60 --action Allow --ip-address 203.0.113.7/32 --output none");
        commands[2].ShouldBe("az webapp config access-restriction remove --name app --resource-group rg --slot release --rule-name gate --output none");
        commands[3].ShouldBe("az webapp config access-restriction show --name app --resource-group rg --slot release --query ipSecurityRestrictions[].name --output tsv");
    }

    [Fact]
    public async Task TemporaryAccessRule_CidrIsKeptAsIs_AndNoSlotOmitsSlotFlag()
    {
        CreateAppService().TemporaryAccessRule("app", "rg", ip: "10.0.0.0/24");

        await RunAsync();

        var add = _executor.ExecutedCommands.Single(c => c.HasArg("add"));
        add.GetArgValue("--ip-address").ShouldBe("10.0.0.0/24");
        add.GetArgValue("--rule-name").ShouldBe("ando-deploy");
        add.GetArgValue("--priority").ShouldBe("50");
        add.HasArg("--slot").ShouldBeFalse();
    }

    [Fact]
    public async Task TemporaryAccessRule_RuleStillPresentAfterRemoval_FailsBuild()
    {
        _executor.CommandOutputs["az"] = "other-rule\nando-deploy";
        CreateAppService().TemporaryAccessRule("app", "rg", ip: "1.2.3.4");

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Access rule ando-deploy is still on app"));
    }

    [Fact]
    public async Task TemporaryAccessRule_AddFails_StillRunsRemovalAndFailsBuild()
    {
        _executor.ConditionalFailures["az"] = args => args.Contains("add");
        CreateAppService().TemporaryAccessRule("app", "rg", ip: "1.2.3.4");

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        // remove, add (fails), then the finally's remove and show.
        _executor.ExecutedCommands.Select(c => c.Args[3]).ShouldBe(["remove", "add", "remove", "show"]);
        result.StepResults.ShouldContain(r => r.StepName == "AppService.RemoveAccessRule" && r.Cleanup && r.Success);
    }

    [Fact]
    public async Task TemporaryAccessRule_InsideBlock_RemovesRuleWhenBlockEnds_BeforeLaterSteps()
    {
        var ando = CreateAndo();
        var appService = CreateAppService();
        var order = new List<string>();
        ando.Block("eu", () =>
        {
            appService.TemporaryAccessRule("app", "rg", ip: "1.2.3.4");
            _registry.Register("work", () => { order.Add("work"); return Task.FromResult(true); });
        });
        _registry.Register("after", () =>
        {
            order.Add($"after (az calls so far: {_executor.ExecutedCommands.Count})");
            return Task.FromResult(true);
        });

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        order.ShouldBe(["work", "after (az calls so far: 4)"]);
    }

    // ---- CommandStep reads --------------------------------------------------

    [Fact]
    public async Task Show_RunsWebappShowAsQuietJson()
    {
        _executor.SimulatedOutput = """{"state":"Running"}""";

        var show = CreateAppService().Show("app", "rg", "release");
        await RunAsync();

        _registry.Steps[0].Name.ShouldBe("AppService.Show");
        _registry.Steps[0].Context.ShouldBe("app");
        var command = _executor.ExecutedCommands.ShouldHaveSingleItem();
        command.CommandLine.ShouldBe("az webapp show --name app --resource-group rg --slot release --output json");
        command.Options!.SuppressOutput.ShouldBeTrue();
        show.Json("state").ShouldBe("Running");
    }

    [Fact]
    public async Task Show_WithoutResourceGroupOrSlot_OmitsThoseFlags()
    {
        CreateAppService().Show("app");

        await RunAsync();

        _executor.LastCommand!.CommandLine.ShouldBe("az webapp show --name app --output json");
    }

    [Fact]
    public async Task GetHostName_QueriesDefaultHostNameAsTsv()
    {
        _executor.SimulatedOutput = "app-release.azurewebsites.net\n";

        var host = CreateAppService().GetHostName("app", "rg", "release");
        await RunAsync();

        _registry.Steps[0].Name.ShouldBe("AppService.GetHostName");
        _executor.LastCommand!.CommandLine.ShouldBe(
            "az webapp show --name app --resource-group rg --slot release --query defaultHostName --output tsv");
        host.Value.ShouldBe("app-release.azurewebsites.net");
        host.ShouldBeAssignableTo<CommandStep>();
    }

    [Fact]
    public async Task AccessRestrictions_ShowsRulesAsQuietJson()
    {
        _executor.SimulatedOutput = """{"ipSecurityRestrictionsDefaultAction":"Deny"}""";

        var rules = CreateAppService().AccessRestrictions("app", "rg", "release");
        await RunAsync();

        _registry.Steps[0].Name.ShouldBe("AppService.AccessRestrictions");
        var command = _executor.ExecutedCommands.ShouldHaveSingleItem();
        command.CommandLine.ShouldBe(
            "az webapp config access-restriction show --name app --resource-group rg --slot release --output json");
        command.Options!.SuppressOutput.ShouldBeTrue();
        rules.Json("ipSecurityRestrictionsDefaultAction").ShouldBe("Deny");
    }
}
