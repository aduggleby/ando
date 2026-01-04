// =============================================================================
// FunctionsDeployOptions.cs
//
// Summary: Fluent configuration options for Azure Functions deployments.
//
// FunctionsDeployOptions provides a fluent builder pattern for configuring
// Azure Functions deployments. Options include deployment slot, runtime settings,
// and deployment-specific configuration.
//
// Design Decisions:
// - Fluent builder pattern (methods return `this`) for readable configuration
// - Deployment slot support for staging/preview deployments
// - Supports both zip deploy and publish profiles
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Fluent options for configuring Azure Functions deployments.
/// </summary>
public class FunctionsDeployOptions
{
    /// <summary>Deployment slot name (e.g., "staging", "preview").</summary>
    internal string? DeploymentSlot { get; private set; }

    /// <summary>Build output configuration (Release, Debug, etc.).</summary>
    internal string? Configuration { get; private set; }

    /// <summary>Force restart after deployment.</summary>
    internal bool ForceRestart { get; private set; }

    /// <summary>Don't wait for deployment to complete.</summary>
    internal bool NoWait { get; private set; }

    /// <summary>
    /// Sets the deployment slot for the function app.
    /// </summary>
    /// <param name="slot">Deployment slot name (e.g., "staging", "preview").
    /// If not specified, deploys to the production slot.</param>
    public FunctionsDeployOptions WithDeploymentSlot(string slot)
    {
        DeploymentSlot = slot;
        return this;
    }

    /// <summary>
    /// Sets the build configuration for publishing.
    /// </summary>
    /// <param name="configuration">Build configuration (e.g., "Release", "Debug").</param>
    public FunctionsDeployOptions WithConfiguration(string configuration)
    {
        Configuration = configuration;
        return this;
    }

    /// <summary>
    /// Forces a restart of the function app after deployment.
    /// </summary>
    public FunctionsDeployOptions WithForceRestart()
    {
        ForceRestart = true;
        return this;
    }

    /// <summary>
    /// Don't wait for deployment to complete (async deployment).
    /// </summary>
    public FunctionsDeployOptions WithNoWait()
    {
        NoWait = true;
        return this;
    }
}
