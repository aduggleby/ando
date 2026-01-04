// =============================================================================
// AppServiceOperations.cs
//
// Summary: Provides Azure App Service deployment operations for build scripts.
//
// AppServiceOperations handles deploying Azure App Services using Azure CLI.
// Supports zip deploy, slot deployments, slot swapping, and app lifecycle
// management.
//
// Architecture:
// - Zip deploy uses `az webapp deployment source config-zip`
// - Slot swapping uses `az webapp deployment slot swap`
// - Authentication relies on prior Azure.Login* steps
//
// Design Decisions:
// - Follows OperationsBase pattern for step registration
// - Deployment slot is a first-class option for staging deployments
// - Resource group is optional for CLI commands (uses app's default)
// - Consistent API with FunctionsOperations for familiarity
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Provides Azure App Service deployment operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class AppServiceOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Registers a step to deploy an app service using zip deploy.
    /// Requires the app service to already exist in Azure.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="zipPath">Path to the deployment zip file.</param>
    /// <param name="resourceGroup">Optional resource group name. If null, Azure CLI will determine it.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void DeployZip(string appName, string zipPath,
        string? resourceGroup = null, Action<AppServiceDeployOptions>? configure = null)
    {
        var options = new AppServiceDeployOptions();
        configure?.Invoke(options);

        RegisterCommand("AppService.DeployZip", "az",
            () => BuildZipDeployArgs(appName, zipPath, resourceGroup, options),
            appName);
    }

    /// <summary>
    /// Registers a step to deploy to a specific slot and then swap to production.
    /// This enables zero-downtime deployments.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="zipPath">Path to the deployment zip file.</param>
    /// <param name="slotName">Staging slot name (defaults to "staging").</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void DeployWithSwap(string appName, string zipPath,
        string slotName = "staging", string? resourceGroup = null)
    {
        // Step 1: Deploy to the staging slot
        DeployZip(appName, zipPath, resourceGroup, opt => opt.WithDeploymentSlot(slotName));

        // Step 2: Swap staging to production
        SwapSlots(appName, slotName, resourceGroup);
    }

    /// <summary>
    /// Registers a step to swap deployment slots.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="sourceSlot">Source slot name to swap from.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="targetSlot">Target slot name (defaults to "production").</param>
    public void SwapSlots(string appName, string sourceSlot,
        string? resourceGroup = null, string targetSlot = "production")
    {
        RegisterCommand("AppService.SwapSlots", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "deployment", "slot", "swap")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .Add("--slot", sourceSlot)
                .Add("--target-slot", targetSlot)
                .Add("--output", "none"),
            $"{sourceSlot} -> {targetSlot}");
    }

    /// <summary>
    /// Registers a step to restart an app service.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, restarts the production slot.</param>
    public void Restart(string appName, string? resourceGroup = null, string? slot = null)
    {
        RegisterCommand("AppService.Restart", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "restart")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "none"),
            appName);
    }

    /// <summary>
    /// Registers a step to stop an app service.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, stops the production slot.</param>
    public void Stop(string appName, string? resourceGroup = null, string? slot = null)
    {
        RegisterCommand("AppService.Stop", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "stop")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "none"),
            appName);
    }

    /// <summary>
    /// Registers a step to start an app service.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, starts the production slot.</param>
    public void Start(string appName, string? resourceGroup = null, string? slot = null)
    {
        RegisterCommand("AppService.Start", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "start")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "none"),
            appName);
    }

    /// <summary>
    /// Registers a step to show app service information.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void Show(string appName, string? resourceGroup = null)
    {
        RegisterCommand("AppService.Show", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "show")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup),
            appName);
    }

    /// <summary>
    /// Registers a step to list deployment slots for an app service.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void ListSlots(string appName, string? resourceGroup = null)
    {
        RegisterCommand("AppService.ListSlots", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "deployment", "slot", "list")
                .Add("--name", appName)
                .AddIfNotNull("--resource-group", resourceGroup),
            appName);
    }

    /// <summary>
    /// Registers a step to create a new deployment slot.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="slotName">Name for the new slot.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="configurationSource">Optional slot to clone configuration from.</param>
    public void CreateSlot(string appName, string slotName,
        string? resourceGroup = null, string? configurationSource = null)
    {
        RegisterCommand("AppService.CreateSlot", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "deployment", "slot", "create")
                .Add("--name", appName)
                .Add("--slot", slotName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--configuration-source", configurationSource)
                .Add("--output", "none"),
            slotName);
    }

    /// <summary>
    /// Registers a step to delete a deployment slot.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="slotName">Name of the slot to delete.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void DeleteSlot(string appName, string slotName, string? resourceGroup = null)
    {
        RegisterCommand("AppService.DeleteSlot", "az",
            () => new ArgumentBuilder()
                .Add("webapp", "deployment", "slot", "delete")
                .Add("--name", appName)
                .Add("--slot", slotName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .Add("--output", "none"),
            slotName);
    }

    /// <summary>
    /// Builds the argument array for zip deploy command.
    /// </summary>
    private static ArgumentBuilder BuildZipDeployArgs(
        string appName,
        string zipPath,
        string? resourceGroup,
        AppServiceDeployOptions options)
    {
        return new ArgumentBuilder()
            .Add("webapp", "deployment", "source", "config-zip")
            .Add("--name", appName)
            .AddIfNotNull("--resource-group", resourceGroup)
            .Add("--src", zipPath)
            .AddIfNotNull("--slot", options.DeploymentSlot)
            .AddFlag(options.NoWait, "--async")
            .Add("--output", "none");
    }
}
