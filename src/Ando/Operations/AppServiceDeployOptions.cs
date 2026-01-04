// =============================================================================
// AppServiceDeployOptions.cs
//
// Summary: Fluent configuration options for Azure App Service deployments.
//
// AppServiceDeployOptions provides a fluent builder pattern for configuring
// Azure App Service deployments. Options include deployment slot, zip deploy
// settings, and deployment-specific configuration.
//
// Design Decisions:
// - Fluent builder pattern (methods return `this`) for readable configuration
// - Deployment slot support for staging/preview deployments
// - Consistent with FunctionsDeployOptions for familiarity
// =============================================================================

namespace Ando.Operations;

/// <summary>
/// Fluent options for configuring Azure App Service deployments.
/// </summary>
public class AppServiceDeployOptions
{
    /// <summary>Deployment slot name (e.g., "staging", "preview").</summary>
    internal string? DeploymentSlot { get; private set; }

    /// <summary>Don't wait for deployment to complete.</summary>
    internal bool NoWait { get; private set; }

    /// <summary>Restart the app after deployment.</summary>
    internal bool Restart { get; private set; }

    /// <summary>
    /// Sets the deployment slot for the app service.
    /// </summary>
    /// <param name="slot">Deployment slot name (e.g., "staging", "preview").
    /// If not specified, deploys to the production slot.</param>
    public AppServiceDeployOptions WithDeploymentSlot(string slot)
    {
        DeploymentSlot = slot;
        return this;
    }

    /// <summary>
    /// Don't wait for deployment to complete (async deployment).
    /// </summary>
    public AppServiceDeployOptions WithNoWait()
    {
        NoWait = true;
        return this;
    }

    /// <summary>
    /// Restart the app after deployment completes.
    /// </summary>
    public AppServiceDeployOptions WithRestart()
    {
        Restart = true;
        return this;
    }
}
