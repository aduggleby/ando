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
// - Manual install calls disable automatic SDK installation
//
// Design Decisions:
// - Uses standard package managers and official install scripts for reliability
// - Node.js installed via NodeSource for version control
// - npm comes with Node.js but can be upgraded separately
// - .NET SDK installation moved to Dotnet.SdkInstall() in DotnetOperations
// - Calling Install() marks manual installation to disable auto-install
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// Operations for installing Node.js globally in the container.
/// </summary>
public class NodeInstallOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    NodeSdkEnsurer? nodeEnsurer = null)
    : OperationsBase(registry, logger, executorFactory)
{
    private readonly NodeSdkEnsurer? _nodeEnsurer = nodeEnsurer;

    /// <summary>
    /// Installs Node.js globally using NodeSource.
    /// Calling this method disables automatic Node.js installation for subsequent npm operations.
    /// </summary>
    /// <param name="version">Major version (e.g., "20", "22"). Defaults to "22" (current LTS).</param>
    public void Install(string version = "22")
    {
        // Mark manual install to disable auto-install for subsequent npm operations.
        _nodeEnsurer?.MarkManualInstallCalled();

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
public class NpmInstallOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    NodeSdkEnsurer? nodeEnsurer = null)
    : OperationsBase(registry, logger, executorFactory)
{
    private readonly NodeSdkEnsurer? _nodeEnsurer = nodeEnsurer;

    // Helper to get the ensurer as a Func<Task>? for RegisterCommandWithEnsurer.
    private Func<Task>? GetEnsurer() =>
        _nodeEnsurer != null ? () => _nodeEnsurer.EnsureInstalledAsync() : null;

    /// <summary>
    /// Upgrades npm to a specific version globally.
    /// Note: npm is installed with Node.js, so Node.Install() should be called first,
    /// or npm auto-install will ensure Node.js is available.
    /// </summary>
    /// <param name="version">npm version (e.g., "10", "latest"). Defaults to "latest".</param>
    public void ToolInstall(string version = "latest")
    {
        RegisterCommandWithEnsurer("Npm.ToolInstall", "npm",
            () => new ArgumentBuilder()
                .Add("install", "-g", $"npm@{version}"),
            GetEnsurer(),
            version);
    }
}

/// <summary>
/// Operations for installing Bun in the build container.
/// </summary>
public class BunOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Installs Bun into /usr/local/bin, so later steps (including MSBuild targets
    /// run by Dotnet.Publish) find bun on PATH. Skips the download when the
    /// container already has that version. Needs a Debian or Ubuntu image.
    /// Usage: Bun.Install("1.2.23");
    /// </summary>
    /// <param name="version">Exact Bun version, for example "1.2.23", or "latest".</param>
    public void Install(string version = "latest")
    {
        var tag = version == "latest" ? "" : $"bun-v{version.TrimStart('v')}";
        var check = version == "latest"
            ? "command -v bun >/dev/null"
            : $"command -v bun >/dev/null && [ \"$(bun --version)\" = \"{version.TrimStart('v')}\" ]";
        // Download the installer to a file first so a failed download fails the
        // step instead of running an empty script, then check the result.

        RegisterCommand("Bun.Install", "bash",
            () => new ArgumentBuilder()
                .Add("-c")
                .Add("set -eu; " +
                     $"if {check}; then echo \"Bun $(bun --version) already installed\"; exit 0; fi; " +
                     "export DEBIAN_FRONTEND=noninteractive; " +
                     "if ! command -v curl >/dev/null || ! command -v unzip >/dev/null; then " +
                     "apt-get update -qq && apt-get install -y -qq curl unzip ca-certificates; fi; " +
                     "curl -fsSL https://bun.sh/install -o /tmp/ando-bun-install.sh; " +
                     $"BUN_INSTALL=/usr/local bash /tmp/ando-bun-install.sh {tag}; rm -f /tmp/ando-bun-install.sh; " +
                     $"{check} || {{ echo \"Bun {version} was not installed (found $(bun --version 2>/dev/null || echo none))\" >&2; exit 1; }}; " +
                     "bun --version"),
            version);
    }
}
