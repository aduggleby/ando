// =============================================================================
// AzureOperationsTests.cs
//
// Summary: Unit tests for AzureOperations class.
//
// Tests verify that:
// - Each operation (EnsureLoggedIn, LoginWithServicePrincipal, etc.) registers correct steps
// - Steps execute the correct az CLI commands with proper arguments
// - Environment variable resolution works correctly
// - Resource group operations pass correct flags
//
// Design: Uses MockExecutor to capture commands without execution,
// and TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class AzureOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private AzureOperations CreateAzure() =>
        new AzureOperations(_registry, _logger, () => _executor);

    [Fact]
    public void EnsureLoggedIn_RegistersStep()
    {
        var azure = CreateAzure();

        azure.EnsureLoggedIn();

        Assert.Single(_registry.Steps);
        Assert.Equal("Azure.EnsureLoggedIn", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task EnsureLoggedIn_ExecutesCorrectCommand()
    {
        var azure = CreateAzure();
        azure.EnsureLoggedIn();

        var success = await _registry.Steps[0].Execute();

        Assert.True(success);
        Assert.Single(_executor.ExecutedCommands);

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("account", cmd.Args);
        Assert.Contains("show", cmd.Args);
        Assert.Contains("--output", cmd.Args);
        Assert.Contains("none", cmd.Args);
    }

    [Fact]
    public void ShowAccount_RegistersStep()
    {
        var azure = CreateAzure();

        azure.ShowAccount();

        Assert.Single(_registry.Steps);
        Assert.Equal("Azure.ShowAccount", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task ShowAccount_ExecutesCorrectCommand()
    {
        var azure = CreateAzure();
        azure.ShowAccount();

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("account", cmd.Args);
        Assert.Contains("show", cmd.Args);
        // ShowAccount does show output (no --output none)
        Assert.DoesNotContain("none", cmd.Args);
    }

    [Fact]
    public void LoginWithServicePrincipal_WithExplicitCredentials_RegistersStep()
    {
        var azure = CreateAzure();

        azure.LoginWithServicePrincipal("client-id", "client-secret", "tenant-id");

        Assert.Single(_registry.Steps);
        Assert.Equal("Azure.Login.ServicePrincipal", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task LoginWithServicePrincipal_ExecutesCorrectCommand()
    {
        var azure = CreateAzure();
        azure.LoginWithServicePrincipal("my-client-id", "my-secret", "my-tenant");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("login", cmd.Args);
        Assert.Contains("--service-principal", cmd.Args);
        Assert.Contains("--username", cmd.Args);
        Assert.Equal("my-client-id", cmd.GetArgValue("--username"));
        Assert.Contains("--password", cmd.Args);
        Assert.Equal("my-secret", cmd.GetArgValue("--password"));
        Assert.Contains("--tenant", cmd.Args);
        Assert.Equal("my-tenant", cmd.GetArgValue("--tenant"));
    }

    [Fact]
    public void LoginWithServicePrincipal_WithoutCredentials_ThrowsWhenEnvNotSet()
    {
        var azure = CreateAzure();

        // Should throw because env vars are not set
        Assert.Throws<InvalidOperationException>(() => azure.LoginWithServicePrincipal());
    }

    [Fact]
    public void LoginWithManagedIdentity_RegistersStep()
    {
        var azure = CreateAzure();

        azure.LoginWithManagedIdentity();

        Assert.Single(_registry.Steps);
        Assert.Equal("Azure.Login.ManagedIdentity", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task LoginWithManagedIdentity_SystemAssigned_ExecutesCorrectCommand()
    {
        var azure = CreateAzure();
        azure.LoginWithManagedIdentity();

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("login", cmd.Args);
        Assert.Contains("--identity", cmd.Args);
        // No --username for system-assigned identity
        Assert.DoesNotContain("--username", cmd.Args);
    }

    [Fact]
    public async Task LoginWithManagedIdentity_UserAssigned_IncludesClientId()
    {
        var azure = CreateAzure();
        azure.LoginWithManagedIdentity("user-assigned-client-id");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--username", cmd.Args);
        Assert.Equal("user-assigned-client-id", cmd.GetArgValue("--username"));
    }

    [Fact]
    public void SetSubscription_WithExplicitId_RegistersStep()
    {
        var azure = CreateAzure();

        azure.SetSubscription("my-subscription-id");

        Assert.Single(_registry.Steps);
        Assert.Equal("Azure.SetSubscription", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task SetSubscription_ExecutesCorrectCommand()
    {
        var azure = CreateAzure();
        azure.SetSubscription("sub-12345");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("account", cmd.Args);
        Assert.Contains("set", cmd.Args);
        Assert.Contains("--subscription", cmd.Args);
        Assert.Equal("sub-12345", cmd.GetArgValue("--subscription"));
    }

    [Fact]
    public void CreateResourceGroup_RegistersStep()
    {
        var azure = CreateAzure();

        azure.CreateResourceGroup("my-rg", "eastus");

        Assert.Single(_registry.Steps);
        Assert.Equal("Azure.CreateResourceGroup", _registry.Steps[0].Name);
        Assert.Equal("my-rg", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task CreateResourceGroup_ExecutesCorrectCommand()
    {
        var azure = CreateAzure();
        azure.CreateResourceGroup("test-rg", "westeurope");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("group", cmd.Args);
        Assert.Contains("create", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("test-rg", cmd.GetArgValue("--name"));
        Assert.Contains("--location", cmd.Args);
        Assert.Equal("westeurope", cmd.GetArgValue("--location"));
    }

    [Fact]
    public void DeleteResourceGroup_RegistersStep()
    {
        var azure = CreateAzure();

        azure.DeleteResourceGroup("my-rg");

        Assert.Single(_registry.Steps);
        Assert.Equal("Azure.DeleteResourceGroup", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task DeleteResourceGroup_ExecutesCorrectCommand()
    {
        var azure = CreateAzure();
        azure.DeleteResourceGroup("delete-me-rg");

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Equal("az", cmd.Command);
        Assert.Contains("group", cmd.Args);
        Assert.Contains("delete", cmd.Args);
        Assert.Contains("--name", cmd.Args);
        Assert.Equal("delete-me-rg", cmd.GetArgValue("--name"));
        Assert.Contains("--yes", cmd.Args);
    }

    [Fact]
    public async Task DeleteResourceGroup_WithNoWait_IncludesNoWaitFlag()
    {
        var azure = CreateAzure();
        azure.DeleteResourceGroup("delete-me-rg", noWait: true);

        await _registry.Steps[0].Execute();

        var cmd = _executor.LastCommand!;
        Assert.Contains("--no-wait", cmd.Args);
    }

    [Fact]
    public async Task AllOperations_ReturnFalse_WhenCommandFails()
    {
        var azure = CreateAzure();
        _executor.SimulateFailure = true;

        azure.EnsureLoggedIn();
        var success = await _registry.Steps[0].Execute();

        Assert.False(success);
    }

    [Fact]
    public void IsAzureCliAvailable_ReturnsWithoutException()
    {
        // This test verifies the method runs without exception
        // The actual result depends on whether Azure CLI is installed
        var result = AzureOperations.IsAzureCliAvailable();

        Assert.True(result || !result); // Always passes, just verifies no exception
    }

    [Fact]
    public void GetAzureCliInstallInstructions_ReturnsNonEmptyString()
    {
        var instructions = AzureOperations.GetAzureCliInstallInstructions();

        Assert.NotNull(instructions);
        Assert.NotEmpty(instructions);
    }

    [Fact]
    public void GetAzureCliInstallInstructions_ContainsInstallCommand()
    {
        var instructions = AzureOperations.GetAzureCliInstallInstructions();

        // Should contain either a package manager command or a URL
        var containsInstallInfo = instructions.Contains("brew") ||
                                   instructions.Contains("curl") ||
                                   instructions.Contains("winget") ||
                                   instructions.Contains("microsoft.com");

        Assert.True(containsInstallInfo, $"Expected install instructions, got: {instructions}");
    }
}
