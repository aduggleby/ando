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

    /// <summary>Restart the app after a zip deploy (default false).</summary>
    internal bool Restart { get; private set; }

    /// <summary>Timeout for the zip deploy command.</summary>
    internal int TimeoutMs { get; private set; } = AppServiceDeployOptions.DefaultZipDeployTimeoutMs;

    /// <summary>True when WithTimeout was called; then az gets its own --timeout.</summary>
    internal bool TimeoutExplicit { get; private set; }

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

    /// <summary>
    /// Restarts the function app after a zip deploy completes. Without this, DeployZip
    /// passes `--restart false` (OneDeploy restarts by default), so a stopped slot is not
    /// started by the deploy. Not used by Publish.
    /// </summary>
    public FunctionsDeployOptions WithRestart()
    {
        Restart = true;
        return this;
    }

    /// <summary>
    /// Sets the maximum time to wait for zip deploy before reconciliation. Not used by Publish.
    /// </summary>
    /// <param name="timeout">Timeout duration. Must be greater than zero.</param>
    public FunctionsDeployOptions WithTimeout(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero and fit within an Int32 millisecond value.");
        }

        TimeoutMs = Math.Max(1, (int)timeout.TotalMilliseconds);
        TimeoutExplicit = true;
        return this;
    }
}
