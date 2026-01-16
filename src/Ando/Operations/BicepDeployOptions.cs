// =============================================================================
// BicepDeployOptions.cs
//
// Summary: Fluent configuration options for Azure Bicep deployments.
//
// BicepDeployOptions provides a fluent builder pattern for configuring
// Bicep deployments. Options include deployment name, parameters (file or inline),
// and deployment mode.
//
// Design Decisions:
// - Fluent builder pattern (methods return `this`) for readable configuration
// - Inline parameters stored in dictionary for easy argument building
// - Deployment outputs are always captured to the returned BicepDeployment object
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

    /// <summary>Azure App Service deployment slot name (e.g., "staging", "preview").</summary>
    internal string? DeploymentSlot { get; private set; }

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
    /// Sets the deployment slot for Azure App Service or Functions deployments.
    /// The slot name is passed as a "deploymentSlot" parameter to the Bicep template.
    /// </summary>
    /// <param name="slot">Deployment slot name (e.g., "staging", "preview").
    /// Use "production" or omit to deploy to the production slot.</param>
    public BicepDeployOptions WithDeploymentSlot(string slot)
    {
        DeploymentSlot = slot;
        return this;
    }
}
