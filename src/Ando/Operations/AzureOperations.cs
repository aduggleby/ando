// =============================================================================
// AzureOperations.cs
//
// Summary: Provides Azure CLI authentication and account management operations.
//
// AzureOperations handles Azure authentication through three methods:
// - Service Principal (for CI/CD pipelines)
// - Interactive login (assumes `az login` was run)
// - Managed Identity (for Azure-hosted environments)
//
// These operations register steps that execute Azure CLI commands to manage
// authentication state before running Bicep deployments.
//
// Design Decisions:
// - Follows OperationsBase pattern for step registration
// - Environment variables use standard Azure SDK naming conventions
// - EnsureLoggedIn is a verification step, not a login step
// - Service Principal credentials can come from env vars or explicit parameters
// - Service principal / managed identity logins run in the build container
//   against its per-run AZURE_CONFIG_DIR (set by the CLI), so they never read
//   or overwrite the user's ~/.azure profile
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// Provides Azure CLI authentication and account management operations.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class AzureOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    // Standard Azure environment variable names
    private const string EnvClientId = "AZURE_CLIENT_ID";
    private const string EnvClientSecret = "AZURE_CLIENT_SECRET";
    private const string EnvTenantId = "AZURE_TENANT_ID";
    private const string EnvClientCertificatePath = "AZURE_CLIENT_CERTIFICATE_PATH";
    private const string EnvSubscriptionId = "AZURE_SUBSCRIPTION_ID";

    // When truthy, EnsureAuthenticated must authenticate with a service principal
    // and will never fall back to an existing CLI session or interactive login.
    private const string EnvRequireServicePrincipal = "ANDO_REQUIRE_SERVICE_PRINCIPAL";

    // Environment variable the Azure CLI uses to locate its profile directory.
    internal const string EnvAzureConfigDir = "AZURE_CONFIG_DIR";

    /// <summary>
    /// Registers a step to verify the user is logged in to Azure.
    /// Fails the build if not authenticated.
    /// </summary>
    public void EnsureLoggedIn()
    {
        RegisterCommand("Azure.EnsureLoggedIn", "az",
            () => new ArgumentBuilder()
                .Add("account", "show")
                .Add("--output", "none"));
    }

    /// <summary>
    /// Ensures Azure authentication is available, using the best available method:
    /// 1. If service principal credentials are provided (explicit arguments or the
    ///    AZURE_CLIENT_ID/SECRET/TENANT_ID env vars), uses service principal login
    /// 2. If Azure CLI is available and already logged in, uses existing session
    /// 3. If Azure CLI is available but not logged in, runs interactive `az login`
    /// 4. If Azure CLI is not installed, prompts user to install it
    ///
    /// When <paramref name="requireServicePrincipal"/> is true (or the
    /// ANDO_REQUIRE_SERVICE_PRINCIPAL environment variable is truthy), only step 1
    /// is allowed: if no service principal credentials are available the build
    /// fails instead of falling back to an existing CLI session or interactive
    /// login. This guarantees a build always runs as the intended service
    /// principal and never as a developer's personal `az login` session.
    ///
    /// Service principal logins run against the container's per-run
    /// AZURE_CONFIG_DIR, so they never read or overwrite the user's ~/.azure profile.
    /// </summary>
    /// <param name="clientId">Service principal application (client) ID, or null to use AZURE_CLIENT_ID.</param>
    /// <param name="clientSecret">Service principal client secret, or null to use AZURE_CLIENT_SECRET.</param>
    /// <param name="tenantId">Tenant ID, or null to use AZURE_TENANT_ID.</param>
    /// <param name="requireServicePrincipal">When true, never fall back to an existing or interactive session.</param>
    public void EnsureAuthenticated(
        string? clientId = null,
        string? clientSecret = null,
        string? tenantId = null,
        bool requireServicePrincipal = false)
    {
        // Resolve service principal credentials from explicit arguments first,
        // then environment variables.
        var resolvedClientId = clientId ?? EnvironmentHelper.Get(EnvClientId);
        var resolvedClientSecret = clientSecret ?? EnvironmentHelper.Get(EnvClientSecret);
        var resolvedTenantId = tenantId ?? EnvironmentHelper.Get(EnvTenantId);
        var hasCertificate = !string.IsNullOrEmpty(EnvironmentHelper.Get(EnvClientCertificatePath));

        var mustUseServicePrincipal =
            requireServicePrincipal || IsTruthy(EnvironmentHelper.Get(EnvRequireServicePrincipal));

        if (!string.IsNullOrEmpty(resolvedClientId)
            && (!string.IsNullOrEmpty(resolvedClientSecret) || hasCertificate)
            && !string.IsNullOrEmpty(resolvedTenantId))
        {
            // Service principal credentials found - use them.
            Logger.Info("Azure: Using service principal authentication");
            if (!string.IsNullOrEmpty(resolvedClientSecret))
            {
                SecretValues.Add(resolvedClientSecret);
            }
            LoginWithServicePrincipal(resolvedClientId, resolvedClientSecret, resolvedTenantId);
            return;
        }

        // No service principal credentials. If one is required, fail fast rather
        // than silently using whoever is logged in to the Azure CLI.
        if (mustUseServicePrincipal)
        {
            throw new InvalidOperationException(
                "Service principal authentication is required (requireServicePrincipal or "
                + "ANDO_REQUIRE_SERVICE_PRINCIPAL), but no client ID/secret/tenant was provided. "
                + "Pass them explicitly or set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID. "
                + "Refusing to fall back to an existing 'az login' session or interactive login.");
        }

        // No service principal credentials: use the build container's az session,
        // or log in with a device code. Runs at execution time inside the build
        // container, which has its own per-run Azure CLI profile, so this never
        // reads or writes the host's ~/.azure.
        Logger.Info("Azure: No service principal credentials; the build will log in interactively (device code)");
        RegisterInteractiveLogin();
    }

    // Treats "1", "true", "yes", "on" (case-insensitive) as true; everything else false.
    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
    }

    /// <summary>
    /// Registers a step that reuses an existing az session in the build
    /// environment or runs `az login --use-device-code`.
    /// </summary>
    private void RegisterInteractiveLogin()
    {
        Registry.Register("Azure.Login.Interactive", async () =>
        {
            var executor = ExecutorFactory();
            var existing = await executor.ExecuteAsync("az", ["account", "show", "--output", "none"],
                new CommandOptions { SuppressOutput = true, TimeoutMs = 60_000 });
            if (existing.Success)
            {
                Logger.Info("    Using the existing Azure CLI session");
                return true;
            }

            var login = await executor.ExecuteAsync("az", ["login", "--use-device-code", "--output", "none"],
                new CommandOptions { Interactive = true, TimeoutMs = 15 * 60 * 1000 });
            return login.Success;
        });
    }

    /// <summary>
    /// Registers a step that runs `az` with <paramref name="args"/> as the build's
    /// authenticated identity, in the build container. Its stdout is available
    /// to later steps; add "--output", "json" or "--query", "...", "-o", "tsv" as needed.
    /// Usage: var show = Azure.Cli("webapp", "show", "-g", rg, "-n", app, "--slot", "release");
    ///        Ando.When(() => show.Json("siteConfig.ipSecurityRestrictionsDefaultAction") == "Deny", ...);
    /// </summary>
    public CommandStep Cli(params string[] args) =>
        ShellCommandOperations.RegisterCommandStep(Registry, Logger, ExecutorFactory,
            "Azure.Cli", "az", args, string.Join(' ', args.TakeWhile(a => !a.StartsWith('-')).Take(3)));

    /// <summary>Azure virtual machine operations (run-command).</summary>
    public AzureVmOperations Vm => _vm ??= new AzureVmOperations(Registry, Logger, ExecutorFactory);
    private AzureVmOperations? _vm;

    /// <summary>
    /// Registers a step to display current Azure account information.
    /// Useful for debugging and verifying which account/subscription is active.
    /// </summary>
    public void ShowAccount()
    {
        RegisterCommand("Azure.ShowAccount", "az",
            () => new ArgumentBuilder()
                .Add("account", "show"));
    }

    /// <summary>
    /// Registers a step to login with a Service Principal.
    /// Credentials can be passed explicitly or read from environment variables.
    /// If AZURE_CLIENT_CERTIFICATE_PATH is set (a PEM file path with key and certificate inside the build
    /// container, e.g. after Ando.CopyToContainer), the certificate is used
    /// instead of the client secret. Credentials reach az through the
    /// environment of the container command, never as process arguments.
    /// </summary>
    /// <param name="clientId">Application (client) ID, or null to use AZURE_CLIENT_ID env var.</param>
    /// <param name="clientSecret">Client secret, or null to use AZURE_CLIENT_SECRET env var.</param>
    /// <param name="tenantId">Tenant ID, or null to use AZURE_TENANT_ID env var.</param>
    public void LoginWithServicePrincipal(string? clientId = null, string? clientSecret = null, string? tenantId = null)
    {
        // Resolve credentials from parameters or environment variables.
        // This is done at registration time so we fail fast if credentials are missing.
        var resolvedClientId = clientId ?? EnvironmentHelper.GetRequired(EnvClientId, "Azure Client ID");
        var certificatePath = EnvironmentHelper.Get(EnvClientCertificatePath);
        var useCertificate = !string.IsNullOrEmpty(certificatePath);
        var resolvedClientSecret = useCertificate
            ? null
            : clientSecret ?? EnvironmentHelper.GetRequired(EnvClientSecret, "Azure Client Secret");
        var resolvedTenantId = tenantId ?? EnvironmentHelper.GetRequired(EnvTenantId, "Azure Tenant ID");
        if (resolvedClientSecret != null)
        {
            SecretValues.Add(resolvedClientSecret);
        }

        // The login runs in the build container, which has its own per-run
        // AZURE_CONFIG_DIR (set by the CLI), so ~/.azure is never touched.
        Logger.Info("Azure: Logging in with a private per-run Azure CLI profile (your ~/.azure is not used)");

        var environment = new Dictionary<string, string>
        {
            [EnvClientId] = resolvedClientId,
            [EnvTenantId] = resolvedTenantId
        };
        if (useCertificate)
        {
            environment[EnvClientCertificatePath] = certificatePath!;
        }
        else
        {
            environment[EnvClientSecret] = resolvedClientSecret!;
        }

        RegisterCommand("Azure.Login.ServicePrincipal", "sh",
            ServicePrincipalLoginArgs(useCertificate),
            useCertificate ? "certificate" : null,
            environment: environment);
    }

    // The credentials are referenced by variable name so they never appear in a process argument list.
    internal static string[] ServicePrincipalLoginArgs(bool useCertificate) =>
    [
        "-c",
        "az login --service-principal -u \"$AZURE_CLIENT_ID\" "
        + (useCertificate ? "--certificate \"$AZURE_CLIENT_CERTIFICATE_PATH\"" : "-p \"$AZURE_CLIENT_SECRET\"")
        + " --tenant \"$AZURE_TENANT_ID\" --output none"
    ];

    /// <summary>
    /// Registers a step to login with Managed Identity.
    /// Used when running inside Azure VMs, App Service, or other Azure-hosted environments.
    /// </summary>
    /// <param name="clientId">Optional client ID for user-assigned managed identity.
    /// If null, uses system-assigned managed identity.</param>
    public void LoginWithManagedIdentity(string? clientId = null)
    {
        // The login runs in the build container, which has its own per-run
        // AZURE_CONFIG_DIR (set by the CLI), so ~/.azure is never touched.
        Logger.Info("Azure: Logging in with a private per-run Azure CLI profile (your ~/.azure is not used)");

        RegisterCommand("Azure.Login.ManagedIdentity", "az",
            () => new ArgumentBuilder()
                .Add("login", "--identity")
                .AddIfNotNull("--username", clientId)
                .Add("--output", "none"));
    }

    /// <summary>
    /// Registers a step to set the active Azure subscription.
    /// </summary>
    /// <param name="subscriptionId">Subscription ID or name to set as active.
    /// If null, uses AZURE_SUBSCRIPTION_ID env var.</param>
    public void SetSubscription(string? subscriptionId = null)
    {
        var resolvedSubscriptionId = subscriptionId ?? EnvironmentHelper.GetRequired(EnvSubscriptionId, "Azure Subscription ID");

        RegisterCommand("Azure.SetSubscription", "az",
            () => new ArgumentBuilder()
                .Add("account", "set")
                .Add("--subscription", resolvedSubscriptionId)
                .Add("--output", "none"));
    }

    /// <summary>
    /// Registers a step to create a resource group if it doesn't exist.
    /// </summary>
    /// <param name="name">Resource group name.</param>
    /// <param name="location">Azure region (e.g., "eastus", "westeurope").</param>
    public void CreateResourceGroup(string name, string location)
    {
        RegisterCommand("Azure.CreateResourceGroup", "az",
            () => new ArgumentBuilder()
                .Add("group", "create")
                .Add("--name", name)
                .Add("--location", location)
                .Add("--output", "none"),
            name);
    }

    /// <summary>
    /// Checks if Azure CLI is installed and available on the system.
    /// </summary>
    /// <returns>True if Azure CLI is available, false otherwise.</returns>
    public static bool IsAzureCliAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "az",
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
}
