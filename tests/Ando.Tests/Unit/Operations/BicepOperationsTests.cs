// =============================================================================
// BicepOperationsTests.cs
//
// Summary: Unit tests for BicepOperations class.
//
// Tests verify that:
// - DeployToResourceGroup registers correct steps with proper arguments
// - DeployToSubscription uses --location instead of --resource-group
// - Options (parameters, deployment name, mode) are translated to CLI flags
// - Deploy methods return BicepDeployment with captured outputs
// - WhatIf adds --what-if flag
// - Build generates correct bicep build command
//
// Design: Uses MockExecutor to capture commands without execution,
// and TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class BicepOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private BicepOperations CreateBicep() =>
        new BicepOperations(_registry, _logger, () => _executor);

    [Fact]
    public void DeployToResourceGroup_RegistersStep()
    {
        var bicep = CreateBicep();

        bicep.DeployToResourceGroup("my-rg", "./main.bicep");

        Assert.Single(_registry.Steps);
        Assert.Equal("Bicep.DeployToResourceGroup", _registry.Steps[0].Name);
        Assert.Equal("my-rg", _registry.Steps[0].Context);
    }

    [Fact]
    public void DeployToResourceGroup_ReturnsBicepDeployment()
    {
        var bicep = CreateBicep();

        var deployment = bicep.DeployToResourceGroup("my-rg", "./main.bicep");

        Assert.NotNull(deployment);
        Assert.IsType<BicepDeployment>(deployment);
    }

    [Fact]
    public async Task DeployToResourceGroup_ExecutesCorrectCommand()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./infra/main.bicep");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("group", cmd.Args);
        Assert.Contains("create", cmd.Args);
        Assert.Contains("--resource-group", cmd.Args);
        Assert.Equal("test-rg", cmd.GetArgValue("--resource-group"));
        Assert.Contains("--template-file", cmd.Args);
        Assert.Equal("./infra/main.bicep", cmd.GetArgValue("--template-file"));
    }

    [Fact]
    public async Task DeployToResourceGroup_WithName_IncludesDeploymentName()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./main.bicep", o => o.WithName("my-deployment"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("my-deployment", cmd.GetArgValue("--name"));
    }

    [Fact]
    public async Task DeployToResourceGroup_WithParameterFile_IncludesParametersFlag()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./main.bicep",
            o => o.WithParameterFile("./main.parameters.json"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--parameters", cmd.Args);
        Assert.Contains("@./main.parameters.json", cmd.Args);
    }

    [Fact]
    public async Task DeployToResourceGroup_WithInlineParameter_IncludesParameter()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./main.bicep",
            o => o.WithParameter("location", "eastus").WithParameter("sku", "Standard"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("location=eastus", cmd.Args);
        Assert.Contains("sku=Standard", cmd.Args);
    }

    [Fact]
    public async Task DeployToResourceGroup_WithCompleteMode_IncludesModeFlag()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./main.bicep",
            o => o.WithMode(DeploymentMode.Complete));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--mode", cmd.Args);
        Assert.Equal("Complete", cmd.GetArgValue("--mode"));
    }

    [Fact]
    public async Task DeployToResourceGroup_AlwaysQueriesOutputs()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./main.bicep");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--query", cmd.Args);
        Assert.Equal("properties.outputs", cmd.GetArgValue("--query"));
        Assert.Contains("--output", cmd.Args);
        Assert.Equal("json", cmd.GetArgValue("--output"));
    }

    [Fact]
    public async Task DeployToResourceGroup_CapturesOutputsToBicepDeployment()
    {
        var bicep = CreateBicep();
        _executor.SimulatedOutput = """
            {
                "storageAccountName": {
                    "type": "String",
                    "value": "mystorageaccount123"
                },
                "connectionString": {
                    "type": "SecureString",
                    "value": "DefaultEndpointsProtocol=https;..."
                }
            }
            """;

        var deployment = bicep.DeployToResourceGroup("test-rg", "./main.bicep");

        await _registry.Steps[0].Execute();

        Assert.Equal("mystorageaccount123", deployment.GetOutput("storageAccountName"));
        Assert.Equal("DefaultEndpointsProtocol=https;...", deployment.GetOutput("connectionString"));
    }

    [Fact]
    public async Task DeployToResourceGroup_OutputRefResolvesAfterExecution()
    {
        var bicep = CreateBicep();
        _executor.SimulatedOutput = """
            {
                "sqlConnectionString": {
                    "type": "String",
                    "value": "Server=myserver.database.windows.net;..."
                }
            }
            """;

        var deployment = bicep.DeployToResourceGroup("test-rg", "./main.bicep");
        var outputRef = deployment.Output("sqlConnectionString");

        // Before execution, output should be null
        Assert.Null(outputRef.Resolve());

        await _registry.Steps[0].Execute();

        // After execution, output should be available
        Assert.Equal("Server=myserver.database.windows.net;...", outputRef.Resolve());
    }

    [Fact]
    public void DeployToSubscription_RegistersStep()
    {
        var bicep = CreateBicep();

        bicep.DeployToSubscription("eastus", "./main.bicep");

        Assert.Single(_registry.Steps);
        Assert.Equal("Bicep.DeployToSubscription", _registry.Steps[0].Name);
        Assert.Equal("eastus", _registry.Steps[0].Context);
    }

    [Fact]
    public void DeployToSubscription_ReturnsBicepDeployment()
    {
        var bicep = CreateBicep();

        var deployment = bicep.DeployToSubscription("eastus", "./main.bicep");

        Assert.NotNull(deployment);
        Assert.IsType<BicepDeployment>(deployment);
    }

    [Fact]
    public async Task DeployToSubscription_ExecutesCorrectCommand()
    {
        var bicep = CreateBicep();
        bicep.DeployToSubscription("westeurope", "./infra/subscription.bicep");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("deployment", cmd.Args);
        Assert.Contains("sub", cmd.Args);
        Assert.Contains("create", cmd.Args);
        Assert.Contains("--location", cmd.Args);
        Assert.Equal("westeurope", cmd.GetArgValue("--location"));
        // Should NOT have --resource-group
        Assert.DoesNotContain("--resource-group", cmd.Args);
    }

    [Fact]
    public void WhatIf_RegistersStep()
    {
        var bicep = CreateBicep();

        bicep.WhatIf("my-rg", "./main.bicep");

        Assert.Single(_registry.Steps);
        Assert.Equal("Bicep.WhatIf", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task WhatIf_IncludesWhatIfFlag()
    {
        var bicep = CreateBicep();
        bicep.WhatIf("test-rg", "./main.bicep");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--what-if", cmd.Args);
    }

    [Fact]
    public void Build_RegistersStep()
    {
        var bicep = CreateBicep();

        bicep.Build("./main.bicep");

        Assert.Single(_registry.Steps);
        Assert.Equal("Bicep.Build", _registry.Steps[0].Name);
        Assert.Equal("main.bicep", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task Build_ExecutesCorrectCommand()
    {
        var bicep = CreateBicep();
        bicep.Build("./infra/main.bicep");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("bicep", cmd.Args);
        Assert.Contains("build", cmd.Args);
        Assert.Contains("--file", cmd.Args);
        Assert.Equal("./infra/main.bicep", cmd.GetArgValue("--file"));
    }

    [Fact]
    public async Task Build_WithOutputFile_IncludesOutfile()
    {
        var bicep = CreateBicep();
        bicep.Build("./main.bicep", "./output.json");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--outfile", cmd.Args);
        Assert.Equal("./output.json", cmd.GetArgValue("--outfile"));
    }

    [Fact]
    public async Task AllOperations_ReturnFalse_WhenCommandFails()
    {
        var bicep = CreateBicep();
        _executor.SimulateFailure = true;

        bicep.DeployToResourceGroup("test-rg", "./main.bicep");
        var success = await _registry.Steps[0].Execute();

        Assert.False(success);
    }

    [Fact]
    public async Task FluentOptions_CanBeChained()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./main.bicep", o => o
            .WithName("my-deployment")
            .WithParameterFile("./params.json")
            .WithParameter("env", "prod")
            .WithMode(DeploymentMode.Incremental));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--name", cmd.Args);
        Assert.Contains("@./params.json", cmd.Args);
        Assert.Contains("env=prod", cmd.Args);
    }

    [Fact]
    public async Task DeployToResourceGroup_WithDeploymentSlot_IncludesSlotParameter()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./main.bicep",
            o => o.WithDeploymentSlot("staging"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("deploymentSlot=staging", cmd.Args);
    }

    [Fact]
    public async Task DeployToResourceGroup_WithDeploymentSlotAndOtherParams_IncludesAllParameters()
    {
        var bicep = CreateBicep();
        bicep.DeployToResourceGroup("test-rg", "./main.bicep", o => o
            .WithParameter("location", "eastus")
            .WithDeploymentSlot("preview")
            .WithParameter("sku", "Standard"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("location=eastus", cmd.Args);
        Assert.Contains("deploymentSlot=preview", cmd.Args);
        Assert.Contains("sku=Standard", cmd.Args);
    }
}
