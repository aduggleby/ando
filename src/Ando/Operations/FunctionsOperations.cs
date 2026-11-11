// =============================================================================
// FunctionsOperations.cs
//
// Summary: Provides Azure Functions deployment operations for build scripts.
//
// FunctionsOperations handles deploying Azure Functions using both Azure CLI
// and Azure Functions Core Tools. Supports zip deploy, slot deployments,
// and function app management.
//
// Architecture:
// - Zip deploy uses `az functionapp deploy --type zip` (shared with AppService via
//   AzureSiteOperationsBase: restart default false, retries, timeout reconciliation)
// - Publish uses `func azure functionapp publish` (Functions Core Tools)
// - Slot deployments use the --slot parameter
// - Authentication relies on prior Azure.Login* steps
//
// Design Decisions:
// - Mirrors AppServiceOperations; shared step builders live in AzureSiteOperationsBase
// - Supports both Azure CLI and Functions Core Tools approaches
// - Deployment slot is a first-class option for staging deployments
// - Resource group is optional for CLI commands (uses app's default)
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Provides Azure Functions deployment operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class FunctionsOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    Func<TimeSpan, Task>? delayAsync = null)
    : AzureSiteOperationsBase(registry, logger, executorFactory, "functionapp", "Functions", "Functions", delayAsync)
{
    /// <summary>
    /// Registers a step to deploy a function app using zip deploy
    /// (`az functionapp deploy --type zip`). Requires the function app to already exist.
    /// Like AppService.DeployZip it passes `--restart false` unless WithRestart() is set,
    /// retries transient failures and reconciles timeouts against the latest deployment log.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="zipPath">Path to the deployment zip file.</param>
    /// <param name="resourceGroup">Optional resource group name. If null, Azure CLI will determine it.</param>
    /// <param name="configure">Optional deployment options (slot, restart, no-wait, timeout).</param>
    public void DeployZip(string functionAppName, string zipPath,
        string? resourceGroup = null, Action<FunctionsDeployOptions>? configure = null)
    {
        var options = new FunctionsDeployOptions();
        configure?.Invoke(options);

        RegisterZipDeploy(functionAppName, zipPath, resourceGroup,
            new SiteZipDeploy(options.DeploymentSlot, options.Restart, options.NoWait, options.TimeoutMs, options.TimeoutExplicit));
    }

    /// <summary>
    /// Registers a step to publish a function app using Azure Functions Core Tools.
    /// This is the recommended approach for .NET and other compiled functions.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="projectPath">Optional path to the function project directory.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void Publish(string functionAppName, string? projectPath = null,
        Action<FunctionsDeployOptions>? configure = null)
    {
        var options = new FunctionsDeployOptions();
        configure?.Invoke(options);

        RegisterCommand("Functions.Publish", "func",
            () => BuildPublishArgs(functionAppName, options),
            functionAppName,
            projectPath);
    }

    /// <summary>
    /// Registers a step to deploy to a specific slot and then swap to production.
    /// This enables zero-downtime deployments.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="zipPath">Path to the deployment zip file.</param>
    /// <param name="slotName">Staging slot name (defaults to "staging").</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void DeployWithSwap(string functionAppName, string zipPath,
        string slotName = "staging", string? resourceGroup = null)
    {
        // Step 1: Deploy to the staging slot
        DeployZip(functionAppName, zipPath, resourceGroup, opt => opt.WithDeploymentSlot(slotName));

        // Step 2: Swap staging to production
        SwapSlots(functionAppName, slotName, resourceGroup);
    }

    /// <summary>
    /// Registers a step to swap deployment slots. A swap warms up the slot and can
    /// take several minutes, so it defaults to a 30 minute timeout (or
    /// Ando.CommandTimeout when that is longer); .WithTimeout(...) on the result
    /// overrides both.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="sourceSlot">Source slot name to swap from.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="targetSlot">Target slot name (defaults to "production").</param>
    public CommandStep SwapSlots(string functionAppName, string sourceSlot,
        string? resourceGroup = null, string targetSlot = "production") =>
        SwapSlotsStep(functionAppName, sourceSlot, resourceGroup, targetSlot);

    /// <summary>Registers a step to restart a function app.</summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, restarts the production slot.</param>
    public CommandStep Restart(string functionAppName, string? resourceGroup = null, string? slot = null) =>
        ActionStep("restart", functionAppName, resourceGroup, slot);

    /// <summary>Registers a step to stop a function app.</summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, stops the production slot.</param>
    public CommandStep Stop(string functionAppName, string? resourceGroup = null, string? slot = null) =>
        ActionStep("stop", functionAppName, resourceGroup, slot);

    /// <summary>Registers a step to start a function app.</summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, starts the production slot.</param>
    public CommandStep Start(string functionAppName, string? resourceGroup = null, string? slot = null) =>
        ActionStep("start", functionAppName, resourceGroup, slot);

    /// <summary>
    /// Registers a step that reads the function app's (or slot's) `az functionapp show` JSON.
    /// Read fields at execution time with Json(), e.g. show.Json("state").
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, shows the production slot.</param>
    public CommandStep Show(string functionAppName, string? resourceGroup = null, string? slot = null) =>
        ShowStep(functionAppName, resourceGroup, slot);

    /// <summary>
    /// Registers a step that reads the function app's (or slot's) default host name, e.g.
    /// "myfunc-release.azurewebsites.net".
    /// Usage: var host = Functions.GetHostName(app, rg, "release");
    ///        Http.WaitForHealthy(() => $"https://{host.Value}/api/health");
    /// </summary>
    public CommandStep GetHostName(string functionAppName, string? resourceGroup = null, string? slot = null) =>
        HostNameStep(functionAppName, resourceGroup, slot);

    /// <summary>
    /// Registers a step that reads the function app's (or slot's) access restrictions
    /// (`az functionapp config access-restriction show`).
    /// Usage: var rules = Functions.AccessRestrictions(app, rg, "release");
    ///        Ando.When(() => rules.Json("ipSecurityRestrictionsDefaultAction") == "Deny", ...);
    /// </summary>
    public CommandStep AccessRestrictions(string functionAppName, string? resourceGroup = null, string? slot = null) =>
        AccessRestrictionsStep(functionAppName, resourceGroup, slot);

    /// <summary>
    /// Adds an Allow access rule for one IPv4 address to the function app (or slot) and
    /// removes it again when the enclosing block (or the run) ends, whatever happened:
    /// the removal is registered as a finally step, followed by a check that the rule is
    /// gone. Any existing rule with the same name is replaced. Without
    /// <paramref name="ip"/>, uses this machine's public IPv4.
    /// Usage: Functions.TemporaryAccessRule(app, rg, "release", "deploy-gate");
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Resource group name.</param>
    /// <param name="slot">Slot name, or null for the production slot.</param>
    /// <param name="ruleName">Rule name (default "ando-deploy").</param>
    /// <param name="ip">IPv4 address or CIDR; null looks up this machine's public IPv4.</param>
    /// <param name="priority">Rule priority (default 50; lower runs first).</param>
    public void TemporaryAccessRule(string functionAppName, string resourceGroup, string? slot = null,
        string ruleName = "ando-deploy", string? ip = null, int priority = 50) =>
        RegisterTemporaryAccessRule(functionAppName, resourceGroup, slot, ruleName, ip, priority);

    /// <summary>Registers a step to list deployment slots for a function app.</summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void ListSlots(string functionAppName, string? resourceGroup = null) =>
        RegisterListSlots(functionAppName, resourceGroup);

    /// <summary>Registers a step to create a deployment slot.</summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="slotName">Name for the new slot.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="configurationSource">Optional slot to clone configuration from.</param>
    public void CreateSlot(string functionAppName, string slotName,
        string? resourceGroup = null, string? configurationSource = null) =>
        RegisterCreateSlot(functionAppName, slotName, resourceGroup, configurationSource);

    /// <summary>Registers a step to delete a deployment slot.</summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="slotName">Name of the slot to delete.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void DeleteSlot(string functionAppName, string slotName, string? resourceGroup = null) =>
        RegisterDeleteSlot(functionAppName, slotName, resourceGroup);

    /// <summary>
    /// Builds the argument array for func publish command.
    /// </summary>
    private static ArgumentBuilder BuildPublishArgs(
        string functionAppName,
        FunctionsDeployOptions options)
    {
        var args = new ArgumentBuilder()
            .Add("azure", "functionapp", "publish", functionAppName);

        if (!string.IsNullOrEmpty(options.DeploymentSlot))
        {
            args.Add("--slot", options.DeploymentSlot);
        }

        if (!string.IsNullOrEmpty(options.Configuration))
        {
            args.Add("--configuration", options.Configuration);
        }

        if (options.ForceRestart)
        {
            args.Add("--force");
        }

        if (options.NoWait)
        {
            args.Add("--no-build");
        }

        return args;
    }

    /// <summary>
    /// Checks if Azure Functions Core Tools is installed and available.
    /// </summary>
    /// <returns>True if func CLI is available, false otherwise.</returns>
    public static bool IsFuncCliAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "func",
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets OS-specific installation instructions for Azure Functions Core Tools.
    /// </summary>
    /// <returns>Installation command or URL for the current platform.</returns>
    public static string GetFuncCliInstallInstructions()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "  macOS:   brew tap azure/functions && brew install azure-functions-core-tools@4";
        }
        if (OperatingSystem.IsLinux())
        {
            return "  Linux:   npm install -g azure-functions-core-tools@4";
        }
        if (OperatingSystem.IsWindows())
        {
            return "  Windows: winget install Microsoft.Azure.FunctionsCoreTools";
        }
        return "  Visit: https://docs.microsoft.com/azure/azure-functions/functions-run-local";
    }
}
