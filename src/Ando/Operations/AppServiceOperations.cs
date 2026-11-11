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
// - Zip deploy uses `az webapp deploy --type zip`
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
public class AppServiceOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    Func<TimeSpan, Task>? delayAsync = null)
    : AzureSiteOperationsBase(registry, logger, executorFactory, "webapp", "AppService", "App Service", delayAsync)
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

        RegisterZipDeploy(appName, zipPath, resourceGroup,
            new SiteZipDeploy(options.DeploymentSlot, options.Restart, options.NoWait, options.TimeoutMs, options.TimeoutExplicit));
    }

    /// <summary>
    /// Registers steps to zip a published folder and deploy it with zip deploy.
    /// Use this instead of DeployZip when you have a publish directory rather than a
    /// pre-built zip: it archives the folder's CONTENTS at the archive root (no
    /// nested top-level directory), which is the layout zip deploy expects. The zip
    /// step runs in the build container and installs the zip tool if the image does
    /// not already provide it (apt-get; Debian and Ubuntu images). For reproducible
    /// builds, zip at build time and use DeployZip. Deploys use
    /// `az webapp deploy --type zip`.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="folderPath">Path to the published folder to deploy.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="configure">Optional deployment options (slot, restart, no-wait).</param>
    public void DeployFolder(string appName, string folderPath,
        string? resourceGroup = null, Action<AppServiceDeployOptions>? configure = null)
    {
        var folder = folderPath.TrimEnd('/', '\\');
        var zipPath = $"{folder}.zip";

        // Archive the folder contents at the root (cd into the folder, zip "."), so
        // the resulting zip has no nested top-level directory. Written as a sibling
        // file (folder.zip) so it is not included in its own archive.
        RegisterCommand("AppService.ZipFolder", "sh",
            () => new ArgumentBuilder()
                .Add("-c",
                    $"command -v zip >/dev/null 2>&1 || (apt-get update -qq && apt-get install -y -qq zip); cd '{folder}' && zip -rq '{zipPath}' ."),
            folder);

        DeployZip(appName, zipPath, resourceGroup, configure);
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
    /// Registers a step to swap deployment slots. A swap warms up the slot and can
    /// take several minutes, so it defaults to a 30 minute timeout (or
    /// Ando.CommandTimeout when that is longer); .WithTimeout(...) on the result
    /// overrides both.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="sourceSlot">Source slot name to swap from.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="targetSlot">Target slot name (defaults to "production").</param>
    public CommandStep SwapSlots(string appName, string sourceSlot,
        string? resourceGroup = null, string targetSlot = "production") =>
        SwapSlotsStep(appName, sourceSlot, resourceGroup, targetSlot);

    /// <summary>Registers a step to restart an app service.</summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, restarts the production slot.</param>
    public CommandStep Restart(string appName, string? resourceGroup = null, string? slot = null) =>
        ActionStep("restart", appName, resourceGroup, slot);

    /// <summary>Registers a step to stop an app service.</summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, stops the production slot.</param>
    public CommandStep Stop(string appName, string? resourceGroup = null, string? slot = null) =>
        ActionStep("stop", appName, resourceGroup, slot);

    /// <summary>Registers a step to start an app service.</summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, starts the production slot.</param>
    public CommandStep Start(string appName, string? resourceGroup = null, string? slot = null) =>
        ActionStep("start", appName, resourceGroup, slot);

    /// <summary>
    /// Registers a step that reads the app's (or slot's) `az webapp show` JSON.
    /// Read fields at execution time with Json(), e.g. show.Json("state").
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, shows the production slot.</param>
    public CommandStep Show(string appName, string? resourceGroup = null, string? slot = null) =>
        ShowStep(appName, resourceGroup, slot);

    /// <summary>
    /// Registers a step that reads the app's (or slot's) default host name, e.g.
    /// "myapp-release.azurewebsites.net".
    /// Usage: var host = AppService.GetHostName(app, rg, "release");
    ///        Http.WaitForHealthy(() => $"https://{host.Value}/healthz");
    /// </summary>
    public CommandStep GetHostName(string appName, string? resourceGroup = null, string? slot = null) =>
        HostNameStep(appName, resourceGroup, slot);

    /// <summary>
    /// Registers a step that reads the app's (or slot's) access restrictions
    /// (`az webapp config access-restriction show`).
    /// Usage: var rules = AppService.AccessRestrictions(app, rg, "release");
    ///        Ando.When(() => rules.Json("ipSecurityRestrictionsDefaultAction") == "Deny", ...);
    /// </summary>
    public CommandStep AccessRestrictions(string appName, string? resourceGroup = null, string? slot = null) =>
        AccessRestrictionsStep(appName, resourceGroup, slot);

    /// <summary>
    /// Adds an Allow access rule for one IPv4 address to the app (or slot) and
    /// removes it again when the enclosing block (or the run) ends, whatever
    /// happened: the removal is registered as a finally step, followed by a
    /// check that the rule is gone. Any existing rule with the same name is
    /// replaced. Without <paramref name="ip"/>, uses this machine's public IPv4.
    /// Usage: AppService.TemporaryAccessRule(app, rg, "release", "deploy-gate");
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Resource group name.</param>
    /// <param name="slot">Slot name, or null for the production slot.</param>
    /// <param name="ruleName">Rule name (default "ando-deploy").</param>
    /// <param name="ip">IPv4 address or CIDR; null looks up this machine's public IPv4.</param>
    /// <param name="priority">Rule priority (default 50; lower runs first).</param>
    public void TemporaryAccessRule(string appName, string resourceGroup, string? slot = null,
        string ruleName = "ando-deploy", string? ip = null, int priority = 50)
    {
        RegisterTemporaryAccessRule(appName, resourceGroup, slot, ruleName, ip, priority);
    }

    /// <summary>
    /// Registers a step to list deployment slots for an app service.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void ListSlots(string appName, string? resourceGroup = null) =>
        RegisterListSlots(appName, resourceGroup);

    /// <summary>
    /// Registers a step to create a new deployment slot.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="slotName">Name for the new slot.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="configurationSource">Optional slot to clone configuration from.</param>
    public void CreateSlot(string appName, string slotName,
        string? resourceGroup = null, string? configurationSource = null) =>
        RegisterCreateSlot(appName, slotName, resourceGroup, configurationSource);

    /// <summary>
    /// Registers a step to delete a deployment slot.
    /// </summary>
    /// <param name="appName">Name of the Azure App Service.</param>
    /// <param name="slotName">Name of the slot to delete.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void DeleteSlot(string appName, string slotName, string? resourceGroup = null) =>
        RegisterDeleteSlot(appName, slotName, resourceGroup);
}
