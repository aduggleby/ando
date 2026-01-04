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
// - Zip deploy uses `az functionapp deployment source config-zip`
// - Publish uses `func azure functionapp publish` (Functions Core Tools)
// - Slot deployments use the --slot parameter
// - Authentication relies on prior Azure.Login* steps
//
// Design Decisions:
// - Follows OperationsBase pattern for step registration
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
public class FunctionsOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Registers a step to deploy a function app using zip deploy.
    /// Requires the function app to already exist in Azure.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="zipPath">Path to the deployment zip file.</param>
    /// <param name="resourceGroup">Optional resource group name. If null, Azure CLI will determine it.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void DeployZip(string functionAppName, string zipPath,
        string? resourceGroup = null, Action<FunctionsDeployOptions>? configure = null)
    {
        var options = new FunctionsDeployOptions();
        configure?.Invoke(options);

        RegisterCommand("Functions.DeployZip", "az",
            () => BuildZipDeployArgs(functionAppName, zipPath, resourceGroup, options),
            functionAppName);
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
    /// Registers a step to swap deployment slots.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="sourceSlot">Source slot name to swap from.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="targetSlot">Target slot name (defaults to "production").</param>
    public void SwapSlots(string functionAppName, string sourceSlot,
        string? resourceGroup = null, string targetSlot = "production")
    {
        RegisterCommand("Functions.SwapSlots", "az",
            () => new ArgumentBuilder()
                .Add("functionapp", "deployment", "slot", "swap")
                .Add("--name", functionAppName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .Add("--slot", sourceSlot)
                .Add("--target-slot", targetSlot)
                .Add("--output", "none"),
            $"{sourceSlot} -> {targetSlot}");
    }

    /// <summary>
    /// Registers a step to restart a function app.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, restarts the production slot.</param>
    public void Restart(string functionAppName, string? resourceGroup = null, string? slot = null)
    {
        RegisterCommand("Functions.Restart", "az",
            () => new ArgumentBuilder()
                .Add("functionapp", "restart")
                .Add("--name", functionAppName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "none"),
            functionAppName);
    }

    /// <summary>
    /// Registers a step to stop a function app.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, stops the production slot.</param>
    public void Stop(string functionAppName, string? resourceGroup = null, string? slot = null)
    {
        RegisterCommand("Functions.Stop", "az",
            () => new ArgumentBuilder()
                .Add("functionapp", "stop")
                .Add("--name", functionAppName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "none"),
            functionAppName);
    }

    /// <summary>
    /// Registers a step to start a function app.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    /// <param name="slot">Optional slot name. If null, starts the production slot.</param>
    public void Start(string functionAppName, string? resourceGroup = null, string? slot = null)
    {
        RegisterCommand("Functions.Start", "az",
            () => new ArgumentBuilder()
                .Add("functionapp", "start")
                .Add("--name", functionAppName)
                .AddIfNotNull("--resource-group", resourceGroup)
                .AddIfNotNull("--slot", slot)
                .Add("--output", "none"),
            functionAppName);
    }

    /// <summary>
    /// Registers a step to show function app information.
    /// </summary>
    /// <param name="functionAppName">Name of the Azure Function App.</param>
    /// <param name="resourceGroup">Optional resource group name.</param>
    public void Show(string functionAppName, string? resourceGroup = null)
    {
        RegisterCommand("Functions.Show", "az",
            () => new ArgumentBuilder()
                .Add("functionapp", "show")
                .Add("--name", functionAppName)
                .AddIfNotNull("--resource-group", resourceGroup),
            functionAppName);
    }

    /// <summary>
    /// Builds the argument array for zip deploy command.
    /// </summary>
    private static ArgumentBuilder BuildZipDeployArgs(
        string functionAppName,
        string zipPath,
        string? resourceGroup,
        FunctionsDeployOptions options)
    {
        return new ArgumentBuilder()
            .Add("functionapp", "deployment", "source", "config-zip")
            .Add("--name", functionAppName)
            .AddIfNotNull("--resource-group", resourceGroup)
            .Add("--src", zipPath)
            .AddIfNotNull("--slot", options.DeploymentSlot)
            .AddFlag(options.NoWait, "--async")
            .Add("--output", "none");
    }

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
