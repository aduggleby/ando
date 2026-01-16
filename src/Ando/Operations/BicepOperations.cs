// =============================================================================
// BicepOperations.cs
//
// Summary: Provides Azure Bicep deployment operations for build scripts.
//
// BicepOperations handles deploying Azure infrastructure using Bicep templates.
// Supports both resource group and subscription-level deployments.
// Returns BicepDeployment objects with strongly-typed access to outputs.
//
// Architecture:
// - Resource group deployments use `az deployment group create`
// - Subscription deployments use `az deployment sub create`
// - Outputs are captured via JSON query and stored in BicepDeployment.Outputs
// - What-if preview uses the same commands with --what-if flag
//
// Design Decisions:
// - Deploy methods return BicepDeployment for strongly-typed output access
// - Outputs are always captured (no opt-in needed)
// - Deployment name defaults to template filename if not specified
// - Parameters can be provided via file, inline, or both
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Provides Azure Bicep deployment operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class BicepOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{

    /// <summary>
    /// Registers a step to deploy a Bicep template to a resource group.
    /// Returns a BicepDeployment object for accessing deployment outputs.
    /// </summary>
    /// <param name="resourceGroup">Target resource group name.</param>
    /// <param name="templateFile">Path to the Bicep template file.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    /// <returns>A BicepDeployment object. Use deployment.Output("name") to get output references.</returns>
    public BicepDeployment DeployToResourceGroup(string resourceGroup, string templateFile,
        Action<BicepDeployOptions>? configure = null)
    {
        var options = new BicepDeployOptions();
        configure?.Invoke(options);

        var deployment = new BicepDeployment();
        var args = BuildDeploymentArgs(templateFile, options);

        RegisterDeploymentStep("Bicep.DeployToResourceGroup",
            "az", ["deployment", "group", "create", "--resource-group", resourceGroup, .. args.Build()],
            deployment, resourceGroup);

        return deployment;
    }

    /// <summary>
    /// Registers a step to deploy a Bicep template at subscription scope.
    /// Useful for creating resource groups and subscription-level resources.
    /// Returns a BicepDeployment object for accessing deployment outputs.
    /// </summary>
    /// <param name="location">Azure region for the deployment metadata.</param>
    /// <param name="templateFile">Path to the Bicep template file.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    /// <returns>A BicepDeployment object. Use deployment.Output("name") to get output references.</returns>
    public BicepDeployment DeployToSubscription(string location, string templateFile,
        Action<BicepDeployOptions>? configure = null)
    {
        var options = new BicepDeployOptions();
        configure?.Invoke(options);

        var deployment = new BicepDeployment();
        var args = BuildDeploymentArgs(templateFile, options);

        RegisterDeploymentStep("Bicep.DeployToSubscription",
            "az", ["deployment", "sub", "create", "--location", location, .. args.Build()],
            deployment, location);

        return deployment;
    }

    /// <summary>
    /// Registers a step to preview what would be deployed (what-if analysis).
    /// Does not actually deploy any resources.
    /// </summary>
    /// <param name="resourceGroup">Target resource group name.</param>
    /// <param name="templateFile">Path to the Bicep template file.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void WhatIf(string resourceGroup, string templateFile,
        Action<BicepDeployOptions>? configure = null)
    {
        var options = new BicepDeployOptions();
        configure?.Invoke(options);

        var args = BuildDeploymentArgs(templateFile, options)
            .Add("--resource-group", resourceGroup)
            .Add("--what-if");

        RegisterCommand("Bicep.WhatIf", "az",
            ["deployment", "group", "create", .. args.Build()],
            resourceGroup);
    }

    /// <summary>
    /// Registers a step to compile a Bicep file to ARM JSON.
    /// Useful for validation or when you need the ARM template.
    /// </summary>
    /// <param name="templateFile">Path to the Bicep template file.</param>
    /// <param name="outputFile">Optional output path. Defaults to templateFile with .json extension.</param>
    public void Build(string templateFile, string? outputFile = null)
    {
        RegisterCommand("Bicep.Build", "az",
            () => new ArgumentBuilder()
                .Add("bicep", "build")
                .Add("--file", templateFile)
                .AddIfNotNull("--outfile", outputFile),
            Path.GetFileName(templateFile));
    }

    /// <summary>
    /// Builds the common arguments for deployment commands.
    /// Always requests JSON output for capturing outputs.
    /// </summary>
    private static ArgumentBuilder BuildDeploymentArgs(string templateFile, BicepDeployOptions options)
    {
        var args = new ArgumentBuilder()
            .Add("--template-file", templateFile)
            .AddIfNotNull("--name", options.DeploymentName);

        // Add parameter file if specified
        if (options.ParameterFile != null)
        {
            args.Add("--parameters", $"@{options.ParameterFile}");
        }

        // Add inline parameters
        foreach (var param in options.Parameters)
        {
            args.Add("--parameters", $"{param.Key}={param.Value}");
        }

        // Add deployment slot if specified
        if (!string.IsNullOrEmpty(options.DeploymentSlot))
        {
            args.Add("--parameters", $"deploymentSlot={options.DeploymentSlot}");
        }

        // Add deployment mode if not incremental (the default)
        if (options.Mode == DeploymentMode.Complete)
        {
            args.Add("--mode", "Complete");
        }

        // Always request JSON output for capturing outputs
        args.Add("--query", "properties.outputs");
        args.Add("--output", "json");

        return args;
    }

    /// <summary>
    /// Registers a deployment step that captures outputs to the BicepDeployment object.
    /// </summary>
    private void RegisterDeploymentStep(string stepName, string command, string[] args,
        BicepDeployment deployment, string? context)
    {
        Registry.Register(stepName, async () =>
        {
            var result = await ExecutorFactory().ExecuteAsync(command, args);

            if (result.Success && !string.IsNullOrEmpty(result.Output))
            {
                AzureOutputCapture.CaptureDeploymentOutputs(result.Output, deployment, Logger);
            }

            return result.Success;
        }, context);
    }
}
