// =============================================================================
// BicepValidateWhatIfTests.cs
//
// Summary: Tests for Bicep validate, structured what-if, additive parameter files
// and secure parameters.
//
// The secure parameters file is written through `sh -c` with the JSON in an
// environment variable, so the tests check that the value is only ever in the
// environment of that one command, never on an argv, and that cleanup also runs
// when az fails.
// =============================================================================

using System.Text.Json;
using Ando.Execution;
using Ando.Logging;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Collection("SecretValues")]
[Trait("Category", "Unit")]
public class BicepValidateWhatIfTests
{
    private const string Secret = "s3cr3t-Value/with+chars\"q";
    private const string EnvName = "ANDO_BICEP_SECURE_PARAMETERS";

    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private BicepOperations Bicep() => new(_registry, _logger, () => _executor);

    private ExecutedCommand Az() => _executor.GetCommands("az").Single();

    private static string Joined(ExecutedCommand c) => string.Join(' ', c.Args);

    // Realistic group-scope output of `az deployment group what-if --no-pretty-print -o json`.
    private const string GroupJson = """
        {
          "changes": [
            { "after": { "type": "Microsoft.Storage/storageAccounts" }, "before": null, "changeType": "Create",
              "delta": null, "resourceId": "/subscriptions/0000/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/new1", "unsupportedReason": null },
            { "changeType": "Modify", "resourceId": "/subscriptions/0000/resourceGroups/rg/providers/Microsoft.Web/sites/app",
              "delta": [ { "path": "properties.siteConfig", "propertyChangeType": "Modify", "children": [] }, { "path": "tags.env", "propertyChangeType": "Create" } ],
              "someFutureField": { "x": 1 } },
            { "changeType": "Modify", "resourceId": "/subscriptions/0000/resourceGroups/rg/providers/Microsoft.Web/sites/app2", "delta": [ { "path": "tags.a" } ] },
            { "changeType": "NoChange", "resourceId": "/subscriptions/0000/resourceGroups/rg/providers/Microsoft.KeyVault/vaults/kv" },
            { "changeType": "Ignore", "resourceId": "/subscriptions/0000/resourceGroups/rg/providers/Microsoft.Network/virtualNetworks/vnet" }
          ],
          "error": null,
          "status": "Succeeded"
        }
        """;

    // Realistic subscription-scope output.
    private const string SubJson = """
        {
          "changes": [
            { "changeType": "Create", "resourceId": "/subscriptions/0000/resourceGroups/new-rg", "delta": null },
            { "changeType": "Delete", "resourceId": "/subscriptions/0000/resourceGroups/old-rg" }
          ],
          "status": "Succeeded"
        }
        """;

    // ---- Arguments ----

    [Fact]
    public async Task Validate_Group_Args_NoOutputQuery()
    {
        Bicep().Validate("my-rg", "./main.bicep", o => o.WithName("n").WithMode(DeploymentMode.Complete));
        await _registry.Steps[0].Execute();

        Joined(Az()).ShouldBe("deployment group validate --resource-group my-rg --template-file ./main.bicep --name n --mode Complete");
        _registry.Steps[0].Name.ShouldBe("Bicep.Validate");
        _registry.Steps[0].Context.ShouldBe("my-rg");
    }

    [Fact]
    public async Task ValidateSubscription_Args_NoModeNoQuery()
    {
        Bicep().ValidateSubscription("westeurope", "./sub.bicep", o => o.WithMode(DeploymentMode.Incremental));
        await _registry.Steps[0].Execute();

        Joined(Az()).ShouldBe("deployment sub validate --location westeurope --template-file ./sub.bicep");
        _registry.Steps[0].Name.ShouldBe("Bicep.ValidateSubscription");
        _registry.Steps[0].Context.ShouldBe("westeurope");
    }

    [Fact]
    public async Task Validate_Failure_FailsStep()
    {
        _executor.FailingCommands.Add("az");
        Bicep().Validate("my-rg", "./main.bicep");

        (await _registry.Steps[0].Execute()).ShouldBeFalse();
    }

    [Fact]
    public async Task WhatIf_Group_Args()
    {
        _executor.SimulatedOutput = GroupJson;
        Bicep().WhatIf("my-rg", "./main.bicep", o => o.WithParameterFile("a.json").WithParameter("k", "v").WithMode(DeploymentMode.Complete));
        await _registry.Steps[0].Execute();

        Joined(Az()).ShouldBe(
            "deployment group what-if --resource-group my-rg --template-file ./main.bicep --parameters @a.json --parameters k=v --mode Complete --no-pretty-print --output json");
        _registry.Steps[0].Name.ShouldBe("Bicep.WhatIf");
        _registry.Steps[0].Context.ShouldBe("my-rg");
    }

    [Fact]
    public async Task WhatIfSubscription_Args_NoMode()
    {
        _executor.SimulatedOutput = SubJson;
        Bicep().WhatIfSubscription("westeurope", "./sub.bicep");
        await _registry.Steps[0].Execute();

        Joined(Az()).ShouldBe("deployment sub what-if --location westeurope --template-file ./sub.bicep --no-pretty-print --output json");
        _registry.Steps[0].Name.ShouldBe("Bicep.WhatIfSubscription");
        _registry.Steps[0].Context.ShouldBe("westeurope");
    }

    [Fact]
    public void CompleteMode_AtSubscriptionScope_FailsAtRegistration()
    {
        var b = Bicep();
        void Complete(BicepDeployOptions o) => o.WithMode(DeploymentMode.Complete);

        Should.Throw<InvalidOperationException>(() => b.DeployToSubscription("l", "s.bicep", Complete)).Message.ShouldContain("--mode");
        Should.Throw<InvalidOperationException>(() => b.ValidateSubscription("l", "s.bicep", Complete));
        Should.Throw<InvalidOperationException>(() => b.WhatIfSubscription("l", "s.bicep", Complete));
        _registry.Steps.ShouldBeEmpty();
        // Group scope is unchanged.
        b.DeployToResourceGroup("rg", "m.bicep", Complete);
        b.Validate("rg", "m.bicep", Complete);
        b.WhatIf("rg", "m.bicep", Complete);
        _registry.Steps.Count.ShouldBe(3);
    }

    [Fact]
    public void Bicepparam_WithAnotherParameterFile_FailsAtRegistration()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            Bicep().Validate("rg", "m.bicep", o => o.WithParameterFile("a.bicepparam").WithParameterFile("b.json")));
        ex.Message.ShouldContain(".bicepparam");
        Should.Throw<InvalidOperationException>(() =>
            Bicep().DeployToResourceGroup("rg", "m.bicep", o => o.WithParameterFile("a.json").WithParameterFile("B.BICEPPARAM")));
        Should.Throw<InvalidOperationException>(() =>
            Bicep().WhatIf("rg", "m.bicep", o => o.WithParameterFile("a.bicepparam").WithParameterFile("b.bicepparam")));
    }

    [Fact]
    public void Bicepparam_WithSecureParameter_FailsAtRegistration()
    {
        var ex = Should.Throw<InvalidOperationException>(() =>
            Bicep().DeployToSubscription("l", "s.bicep", o => o.WithParameterFile("a.bicepparam").WithSecureParameter("k", "secret-value-123")));
        ex.Message.ShouldContain("WithSecureParameter");
        _registry.Steps.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bicepparam_AloneOrWithInlineParameters_IsAccepted()
    {
        Bicep().Validate("rg", "m.bicep", o => o.WithParameterFile("a.bicepparam").WithParameter("x", "1"));
        await _registry.Steps[0].Execute();

        Joined(Az()).ShouldContain("--parameters @a.bicepparam --parameters x=1");
    }

    [Fact]
    public async Task Deploy_Args_AreUnchanged()
    {
        Bicep().DeployToResourceGroup("rg", "./m.bicep", o => o.WithName("n").WithParameterFile("p.json")
            .WithParameter("a", "1").WithDeploymentSlot("staging").WithMode(DeploymentMode.Complete));
        Bicep().DeployToSubscription("eastus", "./s.bicep", o => o.WithParameter("a", "1"));
        foreach (var step in _registry.Steps) await step.Execute();

        Joined(_executor.ExecutedCommands[0]).ShouldBe(
            "deployment group create --resource-group rg --template-file ./m.bicep --name n --parameters @p.json --parameters a=1 --parameters deploymentSlot=staging --mode Complete --query properties.outputs --output json");
        Joined(_executor.ExecutedCommands[1]).ShouldBe(
            "deployment sub create --location eastus --template-file ./s.bicep --parameters a=1 --query properties.outputs --output json");
    }

    // ---- Parameter files ----

    [Fact]
    public async Task ParameterFiles_AreAdditiveAndOrdered()
    {
        Bicep().Validate("rg", "./m.bicep", o => o.WithParameterFile("a.json").WithParameter("x", "1").WithParameterFile("b.json"));
        await _registry.Steps[0].Execute();

        Joined(Az()).ShouldContain("--parameters @a.json --parameters @b.json --parameters x=1");
    }

    // ---- Secure parameters ----

    [Theory]
    [InlineData("deploy")]
    [InlineData("deploySub")]
    [InlineData("validate")]
    [InlineData("validateSub")]
    [InlineData("whatif")]
    [InlineData("whatifSub")]
    public async Task SecureParameter_FileWrittenPassedAndDeleted_ForEveryStep(string kind)
    {
        _executor.SimulatedOutput = kind.StartsWith("deploy") ? "{}" : SubJson;
        void Configure(BicepDeployOptions o) => o.WithParameterFile("a.json").WithSecureParameter("edgeKey", Secret).WithSecureParameter("other", "second-secret");
        var b = Bicep();
        switch (kind)
        {
            case "deploy": b.DeployToResourceGroup("rg", "t.bicep", Configure); break;
            case "deploySub": b.DeployToSubscription("loc", "t.bicep", Configure); break;
            case "validate": b.Validate("rg", "t.bicep", Configure); break;
            case "validateSub": b.ValidateSubscription("loc", "t.bicep", Configure); break;
            case "whatif": b.WhatIf("rg", "t.bicep", Configure); break;
            default: b.WhatIfSubscription("loc", "t.bicep", Configure); break;
        }

        (await _registry.Steps[0].Execute()).ShouldBeTrue();

        _executor.ExecutedCommands.Select(c => c.Command).ShouldBe(["sh", "az", "rm"]);
        var write = _executor.ExecutedCommands[0];
        var path = _executor.ExecutedCommands[2].Args.Last();
        path.ShouldStartWith("/tmp/ando-bicep-");
        path.ShouldEndWith(".json");
        Joined(Az()).ShouldContain($"--parameters @a.json --parameters @{path}");
        write.Args[1].ShouldContain("umask 077");
        write.Args[1].ShouldContain(path);

        using var doc = JsonDocument.Parse(write.Options!.Environment[EnvName]);
        var root = doc.RootElement;
        root.GetProperty("$schema").GetString().ShouldBe("https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#");
        root.GetProperty("contentVersion").GetString().ShouldBe("1.0.0.0");
        root.GetProperty("parameters").GetProperty("edgeKey").GetProperty("value").GetString().ShouldBe(Secret);
        root.GetProperty("parameters").GetProperty("other").GetProperty("value").GetString().ShouldBe("second-secret");
    }

    [Fact]
    public async Task SecureParameter_ValueIsNeverOnAnyArgvOrInLogs()
    {
        _executor.SimulatedOutput = GroupJson;
        Bicep().WhatIf("rg", "t.bicep", o => o.WithSecureParameter("edgeKey", Secret));
        await _registry.Steps[0].Execute();

        foreach (var c in _executor.ExecutedCommands)
        {
            c.Args.ShouldAllBe(a => !a.Contains(Secret));
            c.Args.ShouldAllBe(a => !a.Contains("s3cr3t"));
        }
        _logger.AllMessages.ShouldAllBe(m => !m.Contains(Secret));
        // Only the write command carries the value, in its environment.
        _executor.ExecutedCommands.Where(c => c.Options?.Environment.Values.Any(v => v.Contains("s3cr3t")) == true)
            .Select(c => c.Command).ShouldBe(["sh"]);
    }

    [Fact]
    public void SecureParameter_IsRegisteredAsSecretAtTheCall()
    {
        const string value = "unique-secret-for-registration-test";
        new BicepDeployOptions().WithSecureParameter("k", value);

        SecretValues.Redact($"x {value} y").ShouldNotContain(value);
    }

    [Fact]
    public void SecureParameter_RegistersTheJsonEscapedForm()
    {
        const string value = "pa\"ss\\word-with-quote";
        new BicepDeployOptions().WithSecureParameter("k", value);

        // az prints the value the way JSON encodes it: " as \" and \ as \\.
        const string escaped = "pa\\\"ss\\\\word-with-quote";
        SecretValues.Redact($"x {escaped} y {value} z").ShouldBe("x [REDACTED] y [REDACTED] z");
    }

    [Fact]
    public async Task WhatIf_SuppressesStdoutLogging_ButKeepsItForTheResult()
    {
        _executor.SimulatedOutput = SubJson;
        var plan = Bicep().WhatIfSubscription("l", "s.bicep");
        await _registry.Steps[0].Execute();

        Az().Options!.SuppressStdout.ShouldBeTrue();
        Az().Options!.SuppressOutput.ShouldBeFalse();
        plan.Changes.Count.ShouldBe(2);
    }

    [Fact]
    public async Task DeployAndValidate_DoNotSuppressStdout()
    {
        Bicep().Validate("rg", "t.bicep");
        await _registry.Steps[0].Execute();

        (Az().Options?.SuppressStdout ?? false).ShouldBeFalse();
    }

    [Fact]
    public async Task SecureParameter_WriteFailure_LogsTheReason_WithoutTheValue()
    {
        _executor.QueuedResults.Enqueue(CommandResult.Failed(1, output: "sh: can't create /tmp/x.json: Read-only file system"));
        Bicep().Validate("rg", "t.bicep", o => o.WithSecureParameter("k", Secret));

        (await _registry.Steps[0].Execute()).ShouldBeFalse();

        _logger.ErrorMessages.ShouldContain(m => m.Contains("Read-only file system"));
        _logger.AllMessages.ShouldAllBe(m => !m.Contains(Secret));
        _executor.ExecutedCommands[0].Args[1].ShouldContain("2>&1");
    }

    [Fact]
    public async Task SecureParameter_FileIsDeleted_WhenAzFails()
    {
        _executor.FailingCommands.Add("az");
        Bicep().DeployToResourceGroup("rg", "t.bicep", o => o.WithSecureParameter("k", Secret));

        (await _registry.Steps[0].Execute()).ShouldBeFalse();

        _executor.ExecutedCommands.Select(c => c.Command).ShouldBe(["sh", "az", "rm"]);
    }

    [Fact]
    public async Task SecureParameter_WriteFailure_SkipsAzAndStillCleansUp()
    {
        _executor.FailingCommands.Add("sh");
        Bicep().Validate("rg", "t.bicep", o => o.WithSecureParameter("k", Secret));

        (await _registry.Steps[0].Execute()).ShouldBeFalse();

        _executor.ExecutedCommands.Select(c => c.Command).ShouldBe(["sh", "rm"]);
    }

    [Fact]
    public async Task NoSecureParameters_RunsOnlyAz()
    {
        Bicep().Validate("rg", "t.bicep");
        await _registry.Steps[0].Execute();

        _executor.ExecutedCommands.Select(c => c.Command).ShouldBe(["az"]);
    }

    [Fact]
    public async Task SecureFile_NameIsDifferentForEachRun()
    {
        Bicep().Validate("rg", "t.bicep", o => o.WithSecureParameter("k", Secret));
        await _registry.Steps[0].Execute();
        await _registry.Steps[0].Execute();

        var paths = _executor.GetCommands("rm").Select(c => c.Args.Last()).ToList();
        paths.Distinct().Count().ShouldBe(2);
    }

    // ---- What-if parsing ----

    [Fact]
    public async Task WhatIf_ParsesGroupChanges()
    {
        _executor.SimulatedOutput = GroupJson;
        var plan = Bicep().WhatIf("rg", "t.bicep");

        (await _registry.Steps[0].Execute()).ShouldBeTrue();

        plan.Changes.Count.ShouldBe(5);
        plan.Summary.ShouldBe("1 Create, 2 Modify, 1 NoChange, 1 Ignore");
        plan.HasChanges("Delete").ShouldBeFalse();
        plan.HasChanges("create", "Delete").ShouldBeTrue();
        plan.HasChanges().ShouldBeTrue();
        plan.OfType("Modify").Select(c => c.PropertyChanges).ShouldBe([2, 1]);
        plan.Changes[0].ResourceId.ShouldEndWith("storageAccounts/new1");
        plan.Changes[0].PropertyChanges.ShouldBe(0);
        plan.Json.ShouldContain("someFutureField");
        _logger.InfoMessages.ShouldContain("What-if: 1 Create, 2 Modify, 1 NoChange, 1 Ignore");
        _logger.InfoMessages.ShouldContain(m => m.Contains("Create /subscriptions/0000/resourceGroups/rg/providers/Microsoft.Storage/storageAccounts/new1"));
        _logger.InfoMessages.ShouldNotContain(m => m.Contains("NoChange /"));
        _logger.InfoMessages.ShouldNotContain(m => m.Contains("Ignore /"));
    }

    [Fact]
    public async Task WhatIf_ParsesSubscriptionChanges_AndDeleteGate()
    {
        _executor.SimulatedOutput = SubJson;
        var plan = Bicep().WhatIfSubscription("westeurope", "s.bicep");

        (await _registry.Steps[0].Execute()).ShouldBeTrue();

        plan.Summary.ShouldBe("1 Create, 1 Delete");
        plan.HasChanges("Delete").ShouldBeTrue();
    }

    [Fact]
    public async Task WhatIf_NoChanges_SummaryAndHasChanges()
    {
        _executor.SimulatedOutput = """{"changes":[{"changeType":"NoChange","resourceId":"/a"}],"status":"Succeeded"}""";
        var plan = Bicep().WhatIf("rg", "t.bicep");
        await _registry.Steps[0].Execute();

        plan.HasChanges().ShouldBeFalse();
        plan.Summary.ShouldBe("1 NoChange");
    }

    [Fact]
    public async Task WhatIf_ParsesFromTheFirstBraceOfStdout_IgnoringTextBeforeIt()
    {
        _executor.SimulatedOutput = "WARNING: something\n" + SubJson;
        var plan = Bicep().WhatIfSubscription("l", "s.bicep");

        (await _registry.Steps[0].Execute()).ShouldBeTrue();
        plan.Changes.Count.ShouldBe(2);
    }

    [Theory]
    [InlineData("""{"status":"Failed","error":{"code":"InvalidTemplate","message":"Template is bad"}}""", "InvalidTemplate: Template is bad")]
    [InlineData("""{"status":"Failed"}""", "Failed")]
    [InlineData("""{"status":"Succeeded","error":{"code":"X","message":"odd"},"changes":[]}""", "X: odd")]
    [InlineData("not json", "JSON")]
    public async Task WhatIf_FailedStatusErrorOrBadOutput_FailsStepWithMessage(string output, string expected)
    {
        _executor.SimulatedOutput = output;
        var plan = Bicep().WhatIf("rg", "t.bicep");

        (await _registry.Steps[0].Execute()).ShouldBeFalse();

        _logger.ErrorMessages.ShouldContain(m => m.Contains("Bicep.WhatIf failed") && m.Contains(expected));
        Should.Throw<InvalidOperationException>(() => plan.Changes);
    }

    [Fact]
    public async Task WhatIf_AzFailure_FailsStep()
    {
        _executor.FailingCommands.Add("az");
        Bicep().WhatIf("rg", "t.bicep");

        (await _registry.Steps[0].Execute()).ShouldBeFalse();
    }

    [Fact]
    public void WhatIfResult_ReadBeforeRun_Throws()
    {
        var plan = Bicep().WhatIf("rg", "t.bicep");

        Should.Throw<InvalidOperationException>(() => plan.Changes).Message.ShouldContain("Bicep.WhatIf has not run");
        Should.Throw<InvalidOperationException>(() => plan.Summary);
        Should.Throw<InvalidOperationException>(() => plan.Json);
        Should.Throw<InvalidOperationException>(() => plan.HasChanges("Delete"));
        Should.Throw<InvalidOperationException>(() => plan.OfType("Create").ToList());
        plan.HasValue.ShouldBeFalse();
    }

    [Fact]
    public void WhatIfSubscriptionResult_ReadBeforeRun_NamesItsStep()
    {
        var plan = Bicep().WhatIfSubscription("l", "s.bicep");

        Should.Throw<InvalidOperationException>(() => plan.Summary).Message.ShouldContain("Bicep.WhatIfSubscription has not run");
    }

    // ---- Script compile ----

    [Fact]
    public async Task ScriptCompiles_IgnoringAndUsingWhatIfResult()
    {
        var dir = Directory.CreateTempSubdirectory("ando-bicep-").FullName;
        try
        {
            var path = Path.Combine(dir, "build.csando");
            await File.WriteAllTextAsync(path, """
                Bicep.WhatIf("rg", "a.bicep");
                Bicep.WhatIf("rg", "a.bicep", o => o.WithParameterFile("a.json"));
                var plan = Bicep.WhatIf("rg", "a.bicep", o => o.WithParameterFile("a.json").WithParameterFile("b.json").WithSecureParameter("k", "v"));
                Ando.Require(() => !plan.HasChanges("Delete"), "No deletes allowed");
                Ando.Confirm(() => $"Deploy ({plan.Summary})?");
                var subPlan = Bicep.WhatIfSubscription("westeurope", "s.bicep");
                Bicep.Validate("rg", "a.bicep");
                Bicep.ValidateSubscription("westeurope", "s.bicep", o => o.WithSecureParameter("k", "v"));
                """);

            var errors = await new Ando.Scripting.ScriptHost(new TestLogger()).VerifyScriptAsync(path);

            errors.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
