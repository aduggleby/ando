// =============================================================================
// BicepDeployOptions.cs
//
// Summary: Fluent configuration options for Azure Bicep deployments.
//
// BicepDeployOptions provides a fluent builder pattern for configuring
// Bicep deployments. Options include deployment name, parameters (file or inline),
// deployment mode, and output capture settings.
//
// Design Decisions:
// - Fluent builder pattern (methods return `this`) for readable configuration
// - Inline parameters stored in dictionary for easy argument building
// - CaptureOutputs enables automatic population of Context.Vars from deployment outputs
// - Prefix support for output capture prevents naming collisions
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Deployment mode for Azure Resource Manager deployments.
/// </summary>
public enum DeploymentMode
{
    /// <summary>
    /// Only add/update resources. Existing resources not in template are unchanged.
    /// This is the default and safest mode.
    /// </summary>
    Incremental,

    /// <summary>
    /// Delete resources not in template. Use with caution.
    /// </summary>
    Complete
}

/// <summary>
/// Fluent options for configuring Bicep deployments.
/// </summary>
public class BicepDeployOptions
{
    /// <summary>Custom deployment name. Auto-generated if not specified.</summary>
    internal string? DeploymentName { get; private set; }

    /// <summary>Path to parameters file (JSON or Bicep params).</summary>
    internal string? ParameterFile { get; private set; }

    /// <summary>Inline parameters as key-value pairs.</summary>
    internal Dictionary<string, string> Parameters { get; } = [];

    /// <summary>Deployment mode (Incremental or Complete).</summary>
    internal DeploymentMode Mode { get; private set; } = DeploymentMode.Incremental;

    /// <summary>Whether to capture deployment outputs to Context.Vars.</summary>
    internal bool ShouldCaptureOutputs { get; private set; }

    /// <summary>Prefix for captured output variable names.</summary>
    internal string? OutputPrefix { get; private set; }

    /// <summary>
    /// Sets a custom deployment name.
    /// If not specified, Azure generates a name based on the template file.
    /// </summary>
    /// <param name="name">Deployment name.</param>
    public BicepDeployOptions WithName(string name)
    {
        DeploymentName = name;
        return this;
    }

    /// <summary>
    /// Sets the parameters file path.
    /// Supports JSON (.json) and Bicep parameters (.bicepparam) files.
    /// </summary>
    /// <param name="path">Path to parameters file.</param>
    public BicepDeployOptions WithParameterFile(string path)
    {
        ParameterFile = path;
        return this;
    }

    /// <summary>
    /// Adds an inline parameter.
    /// Can be called multiple times to add multiple parameters.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="value">Parameter value.</param>
    public BicepDeployOptions WithParameter(string name, string value)
    {
        Parameters[name] = value;
        return this;
    }

    /// <summary>
    /// Sets the deployment mode.
    /// </summary>
    /// <param name="mode">Incremental (default) or Complete.</param>
    public BicepDeployOptions WithMode(DeploymentMode mode)
    {
        Mode = mode;
        return this;
    }

    /// <summary>
    /// Enables capturing deployment outputs to Context.Vars.
    /// Outputs are stored with their original names, optionally prefixed.
    /// </summary>
    /// <param name="prefix">Optional prefix for output variable names.
    /// For example, prefix "azure_" would store output "sqlConnectionString"
    /// as Context.Vars["azure_sqlConnectionString"].</param>
    public BicepDeployOptions CaptureOutputs(string? prefix = null)
    {
        ShouldCaptureOutputs = true;
        OutputPrefix = prefix;
        return this;
    }
}
