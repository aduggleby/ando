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
// - Service principal / managed identity logins run against an isolated
//   AZURE_CONFIG_DIR so they never read or overwrite the user's ~/.azure
//   profile (see UseIsolatedAzureProfile)
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

    // When truthy, EnsureAuthenticated must authenticate with a service principal
    // and will never fall back to an existing CLI session or interactive login.
    private const string EnvRequireServicePrincipal = "ANDO_REQUIRE_SERVICE_PRINCIPAL";

    // Environment variable the Azure CLI uses to locate its profile directory.
    internal const string EnvAzureConfigDir = "AZURE_CONFIG_DIR";

    // Isolated Azure CLI profile directory for this process, created on the
    // first service principal / managed identity login. Static because the
    // AZURE_CONFIG_DIR environment variable is process-wide.
    private static readonly object ProfileLock = new();
    private static string? _isolatedProfileDir;

    /// <summary>
    /// Switches this process — and every az invocation it spawns — to a private,
    /// freshly created AZURE_CONFIG_DIR. Service credentials are written there
    /// instead of the user's ~/.azure profile, so a build can never overwrite the
    /// developer's personal az session, and no az step in this build can silently
    /// fall back to it. Applied at registration time: as soon as a build declares
    /// service principal or managed identity authentication, the user's session
    /// profile becomes unreachable for the entire run. The directory is removed
    /// on process exit (best effort) since it holds access tokens.
    /// </summary>
    /// <returns>The isolated profile directory path.</returns>
    internal static string UseIsolatedAzureProfile()
    {
        lock (ProfileLock)
        {
            if (_isolatedProfileDir != null)
            {
                return _isolatedProfileDir;
            }

            var dir = Path.Combine(Path.GetTempPath(), $"ando-azure-profile-{Guid.NewGuid():N}");

            // The profile will contain access tokens - restrict it to the current
            // user on platforms that support Unix file modes.
            if (OperatingSystem.IsWindows())
            {
                Directory.CreateDirectory(dir);
            }
            else
            {
                Directory.CreateDirectory(dir,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }

            Environment.SetEnvironmentVariable(EnvAzureConfigDir, dir);

            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                try { Directory.Delete(dir, recursive: true); }
                catch { /* best effort - temp cleanup will get it eventually */ }
            };

            _isolatedProfileDir = dir;
            return dir;
        }
    }

    /// <summary>
    /// Resets the isolated profile state so tests can exercise
    /// <see cref="UseIsolatedAzureProfile"/> in isolation.
    /// Restores AZURE_CONFIG_DIR to the given value (null clears it).
    /// </summary>
    internal static void ResetIsolatedProfileForTests(string? previousConfigDir = null)
    {
        lock (ProfileLock)
        {
            _isolatedProfileDir = null;
            Environment.SetEnvironmentVariable(EnvAzureConfigDir, previousConfigDir);
        }
    }

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
    /// Service principal logins always run against an isolated AZURE_CONFIG_DIR
    /// (see <see cref="UseIsolatedAzureProfile"/>), so they never read or
    /// overwrite the user's ~/.azure profile.
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

        var mustUseServicePrincipal =
            requireServicePrincipal || IsTruthy(EnvironmentHelper.Get(EnvRequireServicePrincipal));

        if (!string.IsNullOrEmpty(resolvedClientId)
            && !string.IsNullOrEmpty(resolvedClientSecret)
            && !string.IsNullOrEmpty(resolvedTenantId))
        {
            // Service principal credentials found - use them.
            Logger.Info("Azure: Using service principal authentication");
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

    // Treats "1", "true", "yes", "on" (case-insensitive) as true; everything else false.
    private static bool IsTruthy(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.Trim().ToLowerInvariant() is "1" or "true" or "yes" or "on";
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

        // Never write service principal credentials into the user's ~/.azure
        // profile - the whole build runs against an isolated profile instead.
        var profileDir = UseIsolatedAzureProfile();
        Logger.Info($"Azure: Using isolated CLI profile at {profileDir} (user's az profile is not touched)");

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
        // Same isolation as service principal login: keep the managed identity
        // session out of the user's ~/.azure profile.
        var profileDir = UseIsolatedAzureProfile();
        Logger.Info($"Azure: Using isolated CLI profile at {profileDir} (user's az profile is not touched)");

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
