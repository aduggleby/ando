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

using System.Runtime.CompilerServices;

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

    /// <summary>Parameter files (JSON or Bicep params), in the order they were added.</summary>
    internal List<string> ParameterFiles { get; } = [];

    /// <summary>Inline parameters as key-value pairs. These go on the az command line.</summary>
    internal Dictionary<string, string> Parameters { get; } = [];

    /// <summary>Secure parameters, written to a temporary parameters file inside the container.</summary>
    internal Dictionary<string, string> SecureParameters { get; } = [];

    /// <summary>Inline parameters resolved when the step runs. They go on the az command line.</summary>
    internal Dictionary<string, Func<string>> ParameterFactories { get; } = [];

    /// <summary>Secure parameters resolved when the step runs; registered as secrets at that point.</summary>
    internal Dictionary<string, Func<string>> SecureParameterFactories { get; } = [];

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
    /// Adds a parameters file. Supports JSON (.json) and Bicep parameters (.bicepparam) files.
    /// Can be called several times: every file becomes its own <c>--parameters @file</c>, in call order,
    /// and az lets later files override values from earlier ones. A <c>.bicepparam</c> file must be the only
    /// parameter file: az allows only inline KEY=VALUE parameters next to it, so combining it with another file
    /// (or with <see cref="WithSecureParameter"/>) fails when the step is registered.
    /// </summary>
    /// <param name="path">Path to parameters file.</param>
    public BicepDeployOptions WithParameterFile(string path)
    {
        ParameterFiles.Add(path);
        return this;
    }

    /// <summary>
    /// Adds an inline parameter.
    /// Can be called multiple times to add multiple parameters.
    /// Inline parameters are passed on the az command line, so they show up in process listings
    /// (on the host, through <c>docker exec</c>). Use them for non-secret values only;
    /// use <see cref="WithSecureParameter"/> for secrets.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="value">Parameter value.</param>
    [OverloadResolutionPriority(1)] // keeps WithParameter("x", null) unambiguous against the Func form
    public BicepDeployOptions WithParameter(string name, string value)
    {
        Parameters[name] = value;
        ParameterFactories.Remove(name);
        return this;
    }

    /// <summary>
    /// Adds an inline parameter whose value is resolved when the step runs, so it can read a
    /// <see cref="Ando.Steps.StepOutput"/>: <c>o.WithParameter("key", () => sshKey.Value)</c>.
    /// Like the string form, the value goes on the az command line: use it for non-secret values only;
    /// use <see cref="WithSecureParameter(string, Func{string})"/> for secrets.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="value">Called when the step runs.</param>
    public BicepDeployOptions WithParameter(string name, Func<string> value)
    {
        ParameterFactories[name] = value;
        Parameters.Remove(name);
        return this;
    }

    /// <summary>
    /// Adds a secret parameter. The value is registered for redaction right away. When the step
    /// runs, ANDO writes all secure parameters to one temporary parameters file in the container
    /// (mode 0600), passes it as the last <c>--parameters @file</c>, and deletes it afterwards, also
    /// when az fails. The value never appears on a command line (host or container) or in logs.
    /// Works for deploy, validate and what-if, at group and subscription scope, but not together with a
    /// <c>.bicepparam</c> file (az allows only inline KEY=VALUE parameters next to one); that fails when the step is registered.
    /// Values shorter than 6 characters are not redacted from logs.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="value">Parameter value.</param>
    [OverloadResolutionPriority(1)] // keeps WithSecureParameter("x", null) unambiguous against the Func form
    public BicepDeployOptions WithSecureParameter(string name, string value)
    {
        RegisterSecret(value);
        SecureParameters[name] = value;
        SecureParameterFactories.Remove(name);
        return this;
    }

    /// <summary>
    /// Adds a secret parameter whose value is resolved when the step runs, so it can read a
    /// <see cref="Ando.Steps.StepOutput"/>: <c>o.WithSecureParameter("key", () => sshKey.Value)</c>, where
    /// <c>var sshKey = Azure.Cli("vm", "show", ...).Quiet();</c>.
    /// Redaction starts only when the Bicep step resolves the value, so the step that produced it may already have
    /// logged it: keep that step quiet. <c>Azure.Cli(...).Quiet()</c> does; <c>Azure.Vm.RunCommand</c> logs every
    /// stdout line and cannot be quieted.
    /// The value is registered for redaction (with its JSON-escaped forms) as soon as it is resolved, before any
    /// command runs, and then handled like <see cref="WithSecureParameter(string, string)"/>: it goes into the
    /// 0600 parameters file and never on a command line. It cannot be combined with a <c>.bicepparam</c> file either.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="value">Called when the step runs.</param>
    public BicepDeployOptions WithSecureParameter(string name, Func<string> value)
    {
        SecureParameterFactories[name] = value;
        SecureParameters.Remove(name);
        return this;
    }

    /// <summary>Registers a secret and the forms az may print it in (JSON-escaped) for redaction.</summary>
    internal static void RegisterSecret(string value)
    {
        Ando.Logging.SecretValues.Add(value);
        // az may print the value JSON-escaped (quotes, backslashes), so register those forms too.
        foreach (var encoder in new[] { System.Text.Encodings.Web.JavaScriptEncoder.Default, System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })
        {
            var json = System.Text.Json.JsonSerializer.Serialize(value, new System.Text.Json.JsonSerializerOptions { Encoder = encoder });
            var escaped = json[1..^1];
            if (escaped != value) Ando.Logging.SecretValues.Add(escaped);
        }
    }

    /// <summary>
    /// Sets the deployment mode. <see cref="DeploymentMode.Complete"/> works at resource group scope only: at
    /// subscription scope (DeployToSubscription, ValidateSubscription, WhatIfSubscription) az has no --mode, so it
    /// fails when the step is registered.
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
