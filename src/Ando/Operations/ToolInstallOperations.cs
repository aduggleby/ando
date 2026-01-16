// =============================================================================
// ToolInstallOperations.cs
//
// Summary: Operations for installing development tools globally in the container.
//
// These operations install tools like Node.js and npm into the build container.
// They are designed to work with base images like Ubuntu that don't have these
// tools pre-installed.
//
// Architecture:
// - Each Install method registers a step that runs installation commands
// - Version parameters default to "latest" if not specified
// - Tools are installed globally so subsequent steps can use them
//
// Design Decisions:
// - Uses standard package managers and official install scripts for reliability
// - Node.js installed via NodeSource for version control
// - npm comes with Node.js but can be upgraded separately
// - .NET SDK installation moved to Dotnet.SdkInstall() in DotnetOperations
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Operations for installing Node.js globally in the container.
/// </summary>
public class NodeInstallOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Installs Node.js globally using NodeSource.
    /// </summary>
    /// <param name="version">Major version (e.g., "20", "22"). Defaults to "22" (current LTS).</param>
    public void Install(string version = "22")
    {
        // Install Node.js via NodeSource setup script.
        // First checks if the correct major version is already installed (for warm containers).
        // If not, installs curl and ca-certificates, then runs NodeSource setup.
        RegisterCommand("Node.Install", "bash",
            () => new ArgumentBuilder()
                .Add("-c")
                .Add($"if command -v node >/dev/null && node -v | grep -q '^v{version}\\.'; then " +
                     $"echo 'Node.js v{version} already installed'; " +
                     $"else " +
                     $"apt-get update && apt-get install -y curl ca-certificates && " +
                     $"curl -fsSL https://deb.nodesource.com/setup_{version}.x | bash - && " +
                     $"apt-get install -y nodejs; " +
                     $"fi"),
            $"v{version}");
    }
}

/// <summary>
/// Operations for managing npm globally in the container.
/// </summary>
public class NpmInstallOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Upgrades npm to a specific version globally.
    /// Note: npm is installed with Node.js, so Node.Install() should be called first.
    /// </summary>
    /// <param name="version">npm version (e.g., "10", "latest"). Defaults to "latest".</param>
    public void Install(string version = "latest")
    {
        RegisterCommand("Npm.InstallGlobal", "npm",
            () => new ArgumentBuilder()
                .Add("install", "-g", $"npm@{version}"),
            version);
    }
}
