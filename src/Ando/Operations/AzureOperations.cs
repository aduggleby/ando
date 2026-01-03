// =============================================================================
// AzureOperations.cs
//
// Summary: Provides Azure CLI authentication and account management operations.
//
// AzureOperations handles Azure authentication through three methods:
// - Service Principal (for CI/CD pipelines)
// - Interactive login (assumes `az login` was run)
// - Managed Identity (for Azure-hosted environments)
//
// These operations register steps that execute Azure CLI commands to manage
// authentication state before running Bicep deployments.
//
// Design Decisions:
// - Follows OperationsBase pattern for step registration
// - Environment variables use standard Azure SDK naming conventions
// - EnsureLoggedIn is a verification step, not a login step
// - Service Principal credentials can come from env vars or explicit parameters
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Provides Azure CLI authentication and account management operations.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class AzureOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    // Standard Azure environment variable names
    private const string EnvClientId = "AZURE_CLIENT_ID";
    private const string EnvClientSecret = "AZURE_CLIENT_SECRET";
    private const string EnvTenantId = "AZURE_TENANT_ID";
    private const string EnvSubscriptionId = "AZURE_SUBSCRIPTION_ID";

    /// <summary>
    /// Registers a step to verify the user is logged in to Azure.
    /// Fails the build if not authenticated.
    /// </summary>
    public void EnsureLoggedIn()
    {
        RegisterCommand("Azure.EnsureLoggedIn", "az",
            () => new ArgumentBuilder()
                .Add("account", "show")
                .Add("--output", "none"));
    }

    /// <summary>
    /// Registers a step to display current Azure account information.
    /// Useful for debugging and verifying which account/subscription is active.
    /// </summary>
    public void ShowAccount()
    {
        RegisterCommand("Azure.ShowAccount", "az",
            () => new ArgumentBuilder()
                .Add("account", "show"));
    }

    /// <summary>
    /// Registers a step to login with a Service Principal.
    /// Credentials can be passed explicitly or read from environment variables.
    /// </summary>
    /// <param name="clientId">Application (client) ID, or null to use AZURE_CLIENT_ID env var.</param>
    /// <param name="clientSecret">Client secret, or null to use AZURE_CLIENT_SECRET env var.</param>
    /// <param name="tenantId">Tenant ID, or null to use AZURE_TENANT_ID env var.</param>
    public void LoginWithServicePrincipal(string? clientId = null, string? clientSecret = null, string? tenantId = null)
    {
        // Resolve credentials from parameters or environment variables.
        // This is done at registration time so we fail fast if credentials are missing.
        var resolvedClientId = clientId ?? GetRequiredEnv(EnvClientId);
        var resolvedClientSecret = clientSecret ?? GetRequiredEnv(EnvClientSecret);
        var resolvedTenantId = tenantId ?? GetRequiredEnv(EnvTenantId);

        RegisterCommand("Azure.Login.ServicePrincipal", "az",
            () => new ArgumentBuilder()
                .Add("login", "--service-principal")
                .Add("--username", resolvedClientId)
                .Add("--password", resolvedClientSecret)
                .Add("--tenant", resolvedTenantId)
                .Add("--output", "none"));
    }

    /// <summary>
    /// Registers a step to login with Managed Identity.
    /// Used when running inside Azure VMs, App Service, or other Azure-hosted environments.
    /// </summary>
    /// <param name="clientId">Optional client ID for user-assigned managed identity.
    /// If null, uses system-assigned managed identity.</param>
    public void LoginWithManagedIdentity(string? clientId = null)
    {
        RegisterCommand("Azure.Login.ManagedIdentity", "az",
            () => new ArgumentBuilder()
                .Add("login", "--identity")
                .AddIfNotNull("--username", clientId)
                .Add("--output", "none"));
    }

    /// <summary>
    /// Registers a step to set the active Azure subscription.
    /// </summary>
    /// <param name="subscriptionId">Subscription ID or name to set as active.
    /// If null, uses AZURE_SUBSCRIPTION_ID env var.</param>
    public void SetSubscription(string? subscriptionId = null)
    {
        var resolvedSubscriptionId = subscriptionId ?? GetRequiredEnv(EnvSubscriptionId);

        RegisterCommand("Azure.SetSubscription", "az",
            () => new ArgumentBuilder()
                .Add("account", "set")
                .Add("--subscription", resolvedSubscriptionId)
                .Add("--output", "none"));
    }

    /// <summary>
    /// Registers a step to create a resource group if it doesn't exist.
    /// </summary>
    /// <param name="name">Resource group name.</param>
    /// <param name="location">Azure region (e.g., "eastus", "westeurope").</param>
    public void CreateResourceGroup(string name, string location)
    {
        RegisterCommand("Azure.CreateResourceGroup", "az",
            () => new ArgumentBuilder()
                .Add("group", "create")
                .Add("--name", name)
                .Add("--location", location)
                .Add("--output", "none"),
            name);
    }

    /// <summary>
    /// Registers a step to delete a resource group.
    /// Use with caution - this deletes all resources in the group.
    /// </summary>
    /// <param name="name">Resource group name.</param>
    /// <param name="noWait">If true, don't wait for deletion to complete.</param>
    public void DeleteResourceGroup(string name, bool noWait = false)
    {
        RegisterCommand("Azure.DeleteResourceGroup", "az",
            () => new ArgumentBuilder()
                .Add("group", "delete")
                .Add("--name", name)
                .Add("--yes")
                .AddFlag(noWait, "--no-wait")
                .Add("--output", "none"),
            name);
    }

    /// <summary>
    /// Gets a required environment variable, throwing if not set.
    /// </summary>
    private static string GetRequiredEnv(string name)
    {
        var value = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
        }
        return value;
    }
}
