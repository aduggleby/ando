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
    private const string EnvSubscriptionId = "AZURE_SUBSCRIPTION_ID";

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
    /// 1. If service principal env vars are set (AZURE_CLIENT_ID, etc.), uses service principal login
    /// 2. If Azure CLI is available and already logged in, uses existing session
    /// 3. If Azure CLI is available but not logged in, runs interactive `az login`
    /// 4. If Azure CLI is not installed, prompts user to install it
    /// </summary>
    public void EnsureAuthenticated()
    {
        // Check if service principal credentials are available.
        var clientId = EnvironmentHelper.Get(EnvClientId);
        var clientSecret = EnvironmentHelper.Get(EnvClientSecret);
        var tenantId = EnvironmentHelper.Get(EnvTenantId);

        if (!string.IsNullOrEmpty(clientId) && !string.IsNullOrEmpty(clientSecret) && !string.IsNullOrEmpty(tenantId))
        {
            // Service principal credentials found - use them.
            Logger.Info("Azure: Using service principal authentication");
            LoginWithServicePrincipal(clientId, clientSecret, tenantId);
            return;
        }

        // No service principal credentials - check if Azure CLI is available.
        if (!IsAzureCliAvailable())
        {
            // Azure CLI not installed - prompt user to install.
            Logger.Warning("Azure CLI is not installed.");
            Logger.Info(GetAzureCliInstallInstructions());

            Console.Write("\nWould you like to install Azure CLI now? (y/N): ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (response == "y" || response == "yes")
            {
                InstallAzureCli();
                // After installation, we need to run az login.
                Logger.Info("Azure CLI installed. Running interactive login...");
                RegisterInteractiveLogin();
            }
            else
            {
                throw new InvalidOperationException(
                    "Azure CLI is required for authentication. Please install it and run 'az login', " +
                    "or set AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, and AZURE_TENANT_ID environment variables.");
            }
            return;
        }

        // Azure CLI is available - check if already logged in.
        if (IsLoggedIn())
        {
            Logger.Info("Azure: Using existing CLI session");
            // Just verify the session is valid.
            EnsureLoggedIn();
        }
        else
        {
            // Not logged in - run interactive login.
            Logger.Info("Azure: Running interactive login...");
            RegisterInteractiveLogin();
        }
    }

    /// <summary>
    /// Checks if the user is currently logged in to Azure CLI.
    /// </summary>
    private static bool IsLoggedIn()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "az",
                Arguments = "account show --output none",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Registers a step to run interactive Azure login.
    /// </summary>
    private void RegisterInteractiveLogin()
    {
        RegisterCommand("Azure.Login.Interactive", "az",
            () => new ArgumentBuilder()
                .Add("login")
                .Add("--output", "none"));
    }

    /// <summary>
    /// Installs Azure CLI using the appropriate method for the current platform.
    /// </summary>
    private void InstallAzureCli()
    {
        Logger.Info("Installing Azure CLI...");

        ProcessStartInfo startInfo;

        if (OperatingSystem.IsMacOS())
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "brew",
                Arguments = "install azure-cli",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }
        else if (OperatingSystem.IsLinux())
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }
        else if (OperatingSystem.IsWindows())
        {
            startInfo = new ProcessStartInfo
            {
                FileName = "winget",
                Arguments = "install Microsoft.AzureCLI --accept-source-agreements --accept-package-agreements",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
        }
        else
        {
            throw new InvalidOperationException(
                "Automatic installation is not supported on this platform. " +
                "Please visit: https://docs.microsoft.com/cli/azure/install-azure-cli");
        }

        using var process = Process.Start(startInfo);
        if (process == null)
        {
            throw new InvalidOperationException("Failed to start Azure CLI installation process.");
        }

        // Stream output to console.
        process.OutputDataReceived += (_, e) => { if (e.Data != null) Logger.Info(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data != null) Logger.Warning(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"Azure CLI installation failed with exit code {process.ExitCode}.");
        }

        Logger.Info("Azure CLI installed successfully.");
    }

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
    /// </summary>
    /// <param name="clientId">Application (client) ID, or null to use AZURE_CLIENT_ID env var.</param>
    /// <param name="clientSecret">Client secret, or null to use AZURE_CLIENT_SECRET env var.</param>
    /// <param name="tenantId">Tenant ID, or null to use AZURE_TENANT_ID env var.</param>
    public void LoginWithServicePrincipal(string? clientId = null, string? clientSecret = null, string? tenantId = null)
    {
        // Resolve credentials from parameters or environment variables.
        // This is done at registration time so we fail fast if credentials are missing.
        var resolvedClientId = clientId ?? EnvironmentHelper.GetRequired(EnvClientId, "Azure Client ID");
        var resolvedClientSecret = clientSecret ?? EnvironmentHelper.GetRequired(EnvClientSecret, "Azure Client Secret");
        var resolvedTenantId = tenantId ?? EnvironmentHelper.GetRequired(EnvTenantId, "Azure Tenant ID");

        RegisterCommand("Azure.Login.ServicePrincipal", "az",
            () => new ArgumentBuilder()
                .Add("login", "--service-principal")
                .Add("--username", resolvedClientId)
                .Add("--password", resolvedClientSecret)
                .Add("--tenant", resolvedTenantId)
                .Add("--output", "none"));
    }

    /// <summary>
    /// Registers a step to login with Managed Identity.
    /// Used when running inside Azure VMs, App Service, or other Azure-hosted environments.
    /// </summary>
    /// <param name="clientId">Optional client ID for user-assigned managed identity.
    /// If null, uses system-assigned managed identity.</param>
    public void LoginWithManagedIdentity(string? clientId = null)
    {
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
    /// Registers a step to delete a resource group.
    /// Use with caution - this deletes all resources in the group.
    /// </summary>
    /// <param name="name">Resource group name.</param>
    /// <param name="noWait">If true, don't wait for deletion to complete.</param>
    public void DeleteResourceGroup(string name, bool noWait = false)
    {
        RegisterCommand("Azure.DeleteResourceGroup", "az",
            () => new ArgumentBuilder()
                .Add("group", "delete")
                .Add("--name", name)
                .Add("--yes")
                .AddFlag(noWait, "--no-wait")
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

    /// <summary>
    /// Gets OS-specific installation instructions for Azure CLI.
    /// </summary>
    /// <returns>Installation command or URL for the current platform.</returns>
    public static string GetAzureCliInstallInstructions()
    {
        if (OperatingSystem.IsMacOS())
        {
            return "  macOS:   brew install azure-cli";
        }
        if (OperatingSystem.IsLinux())
        {
            return "  Linux:   curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash";
        }
        if (OperatingSystem.IsWindows())
        {
            return "  Windows: winget install Microsoft.AzureCLI";
        }
        return "  Visit: https://docs.microsoft.com/cli/azure/install-azure-cli";
    }
}
