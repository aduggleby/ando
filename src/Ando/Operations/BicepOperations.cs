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
// - What-if uses `az deployment group|sub what-if` and returns a BicepWhatIfResult;
//   validate uses `az deployment group|sub validate`
// - Secure parameters go to a temporary 0600 parameters file in the container
//
// Design Decisions:
// - Deploy methods return BicepDeployment for strongly-typed output access
// - Outputs are always captured (no opt-in needed)
// - Deployment name defaults to template filename if not specified
// - Parameters can be provided via file, inline, or both
// =============================================================================

using System.Text.Encodings.Web;
using System.Text.Json;
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
        var deployment = new BicepDeployment();
        RegisterAz("Bicep.DeployToResourceGroup", ["deployment", "group", "create", "--resource-group", resourceGroup],
            templateFile, configure, subscription: false, suffix: OutputQuery, context: resourceGroup,
            handle: result => CaptureOutputs(result, deployment));
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
        var deployment = new BicepDeployment();
        RegisterAz("Bicep.DeployToSubscription", ["deployment", "sub", "create", "--location", location],
            templateFile, configure, subscription: true, suffix: OutputQuery, context: location,
            handle: result => CaptureOutputs(result, deployment));
        return deployment;
    }

    /// <summary>
    /// Registers a step that validates a Bicep template against a resource group
    /// (<c>az deployment group validate</c>). A non-zero exit fails the step.
    /// </summary>
    /// <param name="resourceGroup">Target resource group name.</param>
    /// <param name="templateFile">Path to the Bicep template file.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void Validate(string resourceGroup, string templateFile, Action<BicepDeployOptions>? configure = null) =>
        RegisterAz("Bicep.Validate", ["deployment", "group", "validate", "--resource-group", resourceGroup],
            templateFile, configure, subscription: false, suffix: [], context: resourceGroup,
            handle: result => result.Success);

    /// <summary>
    /// Registers a step that validates a Bicep template at subscription scope
    /// (<c>az deployment sub validate</c>). A non-zero exit fails the step.
    /// </summary>
    /// <param name="location">Azure region for the deployment metadata.</param>
    /// <param name="templateFile">Path to the Bicep template file.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void ValidateSubscription(string location, string templateFile, Action<BicepDeployOptions>? configure = null) =>
        RegisterAz("Bicep.ValidateSubscription", ["deployment", "sub", "validate", "--location", location],
            templateFile, configure, subscription: true, suffix: [], context: location,
            handle: result => result.Success);

    /// <summary>
    /// Registers a step to preview what would be deployed (what-if analysis).
    /// Does not actually deploy any resources. The returned result is filled when the step runs;
    /// read it inside execution-time lambdas such as <c>Ando.Require(() => ...)</c> or
    /// <c>Ando.Confirm(() => ...)</c>.
    /// </summary>
    /// <param name="resourceGroup">Target resource group name.</param>
    /// <param name="templateFile">Path to the Bicep template file.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public BicepWhatIfResult WhatIf(string resourceGroup, string templateFile,
        Action<BicepDeployOptions>? configure = null)
    {
        const string stepName = "Bicep.WhatIf";
        var plan = new BicepWhatIfResult(stepName);
        RegisterAz(stepName, ["deployment", "group", "what-if", "--resource-group", resourceGroup],
            templateFile, configure, subscription: false, suffix: WhatIfFormat, context: resourceGroup, quietStdout: true,
            handle: result => LoadWhatIf(stepName, result, plan));
        return plan;
    }

    /// <summary>
    /// Registers a step to preview what a subscription-scope deployment would change
    /// (<c>az deployment sub what-if</c>). See <see cref="WhatIf"/>.
    /// </summary>
    /// <param name="location">Azure region for the deployment metadata.</param>
    /// <param name="templateFile">Path to the Bicep template file.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public BicepWhatIfResult WhatIfSubscription(string location, string templateFile,
        Action<BicepDeployOptions>? configure = null)
    {
        const string stepName = "Bicep.WhatIfSubscription";
        var plan = new BicepWhatIfResult(stepName);
        RegisterAz(stepName, ["deployment", "sub", "what-if", "--location", location],
            templateFile, configure, subscription: true, suffix: WhatIfFormat, context: location, quietStdout: true,
            handle: result => LoadWhatIf(stepName, result, plan));
        return plan;
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

    private static readonly string[] OutputQuery = ["--query", "properties.outputs", "--output", "json"];
    private static readonly string[] WhatIfFormat = ["--no-pretty-print", "--output", "json"];

    private const string SecureParametersEnv = "ANDO_BICEP_SECURE_PARAMETERS";

    /// <summary>
    /// Builds the template and parameter arguments shared by deploy, validate and what-if.
    /// Order (deploy depends on it): template, name, parameter files, inline parameters, slot,
    /// secure parameters file, mode. Later --parameters override earlier ones in az.
    /// </summary>
    private static ArgumentBuilder BuildDeploymentArgs(string templateFile, BicepDeployOptions options,
        string? secureParameterFile, bool includeMode)
    {
        var args = new ArgumentBuilder()
            .Add("--template-file", templateFile)
            .AddIfNotNull("--name", options.DeploymentName);

        foreach (var file in options.ParameterFiles)
        {
            args.Add("--parameters", $"@{file}");
        }

        // Inline parameters (non-secret: they are on the command line)
        foreach (var param in options.Parameters)
        {
            args.Add("--parameters", $"{param.Key}={param.Value}");
        }

        // Add deployment slot if specified
        if (!string.IsNullOrEmpty(options.DeploymentSlot))
        {
            args.Add("--parameters", $"deploymentSlot={options.DeploymentSlot}");
        }

        if (secureParameterFile != null)
        {
            args.Add("--parameters", $"@{secureParameterFile}");
        }

        // Add deployment mode if not incremental (the default). Subscription-scope
        // validate and what-if have no --mode.
        if (includeMode && options.Mode == DeploymentMode.Complete)
        {
            args.Add("--mode", "Complete");
        }

        return args;
    }

    /// <summary>
    /// Registers an az step. Options are read when the step is registered; the az arguments are
    /// built when it runs, because the secure parameters file has a name chosen at run time.
    /// </summary>
    private void RegisterAz(string stepName, string[] scopeArgs, string templateFile,
        Action<BicepDeployOptions>? configure, bool subscription, string[] suffix, string context,
        Func<CommandResult, bool> handle, bool quietStdout = false)
    {
        var options = new BicepDeployOptions();
        configure?.Invoke(options);
        ValidateOptions(stepName, options, subscription);
        var includeMode = !subscription;
        // quietStdout keeps az's stdout (what-if JSON, which can echo secret values) out of the log;
        // it is still captured into the result, and stderr is still logged.
        var commandOptions = quietStdout ? new CommandOptions { SuppressStdout = true } : null;

        Registry.Register(stepName, async () =>
        {
            string? secureFile = null;
            if (options.SecureParameters.Count > 0)
            {
                secureFile = $"/tmp/ando-bicep-{Guid.NewGuid():N}.json";
                if (!await WriteSecureParametersAsync(secureFile, options.SecureParameters))
                {
                    await DeleteSecureParametersAsync(secureFile);
                    return false;
                }
            }

            try
            {
                string[] args =
                [
                    .. scopeArgs,
                    .. BuildDeploymentArgs(templateFile, options, secureFile, includeMode).Build(),
                    .. suffix,
                ];
                var result = await ExecutorFactory().ExecuteAsync("az", args, commandOptions);
                return handle(result);
            }
            finally
            {
                if (secureFile != null)
                {
                    await DeleteSecureParametersAsync(secureFile);
                }
            }
        }, context);
    }

    // Fails at registration for combinations az rejects, so the script does not get as far as a deployment.
    private static void ValidateOptions(string stepName, BicepDeployOptions options, bool subscription)
    {
        if (subscription && options.Mode == DeploymentMode.Complete)
        {
            throw new InvalidOperationException(
                $"{stepName}: az does not accept --mode at subscription scope, so DeploymentMode.Complete cannot be used here. " +
                "Use a resource group deployment, or remove WithMode.");
        }

        var bicepParam = options.ParameterFiles.Any(f => f.EndsWith(".bicepparam", StringComparison.OrdinalIgnoreCase));
        if (bicepParam && options.ParameterFiles.Count > 1)
        {
            throw new InvalidOperationException(
                $"{stepName}: a .bicepparam file cannot be combined with another parameter file; az allows only inline KEY=VALUE parameters next to it.");
        }
        if (bicepParam && options.SecureParameters.Count > 0)
        {
            throw new InvalidOperationException(
                $"{stepName}: WithSecureParameter cannot be combined with a .bicepparam file; az allows only inline KEY=VALUE parameters next to it. " +
                "Use a .json parameter file with WithSecureParameter, or read the secret in the .bicepparam file (for example with readEnvironmentVariable).");
        }
    }

    /// <summary>
    /// Writes the secure parameters file inside the container without putting values on any command
    /// line: the JSON travels in an environment variable (passed by name, with the value in the
    /// docker client's environment) and the shell builtin printf writes it with a 0600 umask.
    /// </summary>
    private async Task<bool> WriteSecureParametersAsync(string path, Dictionary<string, string> secrets)
    {
        var parameters = secrets.ToDictionary(p => p.Key, p => new Dictionary<string, string> { ["value"] = p.Value });
        var json = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["$schema"] = "https://schema.management.azure.com/schemas/2019-04-01/deploymentParameters.json#",
            ["contentVersion"] = "1.0.0.0",
            ["parameters"] = parameters,
        }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var options = new CommandOptions { SuppressOutput = true };
        options.Environment[SecureParametersEnv] = json;
        var result = await ExecutorFactory().ExecuteAsync("sh",
            ["-c", $"{{ umask 077 && printf '%s' \"${SecureParametersEnv}\" > '{path}' && chmod 600 '{path}'; }} 2>&1"], options);
        if (!result.Success)
        {
            // stderr was folded into stdout above; the value is only in the environment, never in that text.
            var reason = result.Output?.Trim() is { Length: > 0 } text ? text : result.Error;
            Logger.Error("Could not write the secure parameters file in the build container" +
                         (string.IsNullOrWhiteSpace(reason) ? "." : $": {reason}"));
        }
        return result.Success;
    }

    private async Task DeleteSecureParametersAsync(string path)
    {
        var result = await ExecutorFactory().ExecuteAsync("rm", ["-f", "--", path],
            new CommandOptions { SuppressOutput = true });
        if (!result.Success)
        {
            Logger.Warning($"Could not delete the secure parameters file {path} in the build container.");
        }
    }

    private bool LoadWhatIf(string stepName, CommandResult result, BicepWhatIfResult plan)
    {
        if (!result.Success) return false;

        var error = plan.Load(result.Output);
        if (error != null)
        {
            Logger.Error($"{stepName} failed: {error}");
            return false;
        }

        Logger.Info($"What-if: {plan.Summary}");
        foreach (var line in plan.DescribeChanges())
        {
            Logger.Info($"  {line}");
        }
        return true;
    }

    private bool CaptureOutputs(CommandResult result, BicepDeployment deployment)
    {
        if (result.Success && !string.IsNullOrEmpty(result.Output))
        {
            AzureOutputCapture.CaptureDeploymentOutputs(result.Output, deployment, Logger);
        }

        return result.Success;
    }
}
