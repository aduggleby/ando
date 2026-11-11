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

    /// <summary>Restart the app after a zip deploy (default false). OneDeploy only.</summary>
    internal bool Restart { get; private set; }

    /// <summary>True when WithOneDeploy was called.</summary>
    internal bool OneDeploy { get; private set; }

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
    /// Don't wait for deployment to complete (async deployment). For DeployZip this is OneDeploy
    /// only (`--async true`): DeployZip throws unless WithOneDeploy() is also set. For Publish it
    /// maps to `--no-build`.
    /// </summary>
    public FunctionsDeployOptions WithNoWait()
    {
        NoWait = true;
        return this;
    }

    /// <summary>
    /// Deploys the zip with OneDeploy (`az functionapp deploy --type zip`, a preview command that
    /// posts straight to /api/publish) instead of the default
    /// `az functionapp deployment source config-zip`. The default's client-side branches support
    /// every plan: dedicated and Premium (Kudu zip deploy), Linux Consumption (uploads the zip to
    /// storage and sets WEBSITE_RUN_FROM_PACKAGE) and Flex Consumption. OneDeploy fits dedicated,
    /// Premium and Flex plans; do not assume it works on Linux Consumption. Required for
    /// WithRestart() and WithNoWait(). Under OneDeploy `--restart false` is the default.
    /// Not used by Publish.
    /// </summary>
    public FunctionsDeployOptions WithOneDeploy()
    {
        OneDeploy = true;
        return this;
    }

    /// <summary>
    /// Restarts the function app after the zip deploy completes. OneDeploy only: DeployZip throws
    /// unless WithOneDeploy() is also set, because config-zip has no restart option. Without this,
    /// OneDeploy passes `--restart false` (it restarts by default), so a stopped slot is not
    /// started by the deploy. Not used by Publish.
    /// </summary>
    public FunctionsDeployOptions WithRestart()
    {
        Restart = true;
        return this;
    }

    /// <summary>
    /// Sets the maximum time to wait for the zip deploy. Works on both paths: config-zip gets
    /// `--timeout` in seconds, OneDeploy in milliseconds. Not used by Publish.
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
