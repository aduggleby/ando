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

using System.Text.RegularExpressions;
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
    /// With <paramref name="sha256"/> the release zip is downloaded from GitHub
    /// and verified against the hash before it is installed; the step fails on a
    /// mismatch, and it always downloads and verifies (it never trusts a bun that
    /// is already installed).
    /// Usage: Bun.Install("1.2.23");
    ///        Bun.Install("1.2.23", sha256: "&lt;sha256 of bun-linux-x64.zip&gt;");
    /// </summary>
    /// <param name="version">Exact Bun version, for example "1.2.23", or "latest" (not allowed with sha256).</param>
    /// <param name="sha256">
    /// SHA-256 (64 hex characters) of the release zip for the container's architecture
    /// (bun-linux-x64.zip or bun-linux-aarch64.zip; the two hashes differ).
    /// </param>
    public void Install(string version = "latest", string? sha256 = null)
    {
        var exact = version.TrimStart('v');
        if (sha256 != null)
        {
            if (version == "latest" || !ExactVersion.IsMatch(exact))
            {
                throw new ArgumentException(
                    $"Bun.Install with a sha256 needs an exact version such as \"1.2.23\", not \"{version}\".", nameof(version));
            }
            if (!Sha256Hex.IsMatch(sha256))
            {
                throw new ArgumentException("sha256 must be 64 hexadecimal characters.", nameof(sha256));
            }

            RegisterCommand("Bun.Install", "bash",
                () => new ArgumentBuilder().Add("-c").Add(BuildVerifiedInstallScript(exact, sha256.ToLowerInvariant())),
                $"{exact} (sha256)");
            return;
        }

        var tag = version == "latest" ? "" : $"bun-v{exact}";
        var check = version == "latest"
            ? "command -v bun >/dev/null"
            : $"command -v bun >/dev/null && [ \"$(bun --version)\" = \"{exact}\" ]";
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

    private static readonly Regex ExactVersion = new(@"^\d+\.\d+\.\d+(-[0-9A-Za-z.]+)?$", RegexOptions.Compiled);
    private static readonly Regex Sha256Hex = new("^[0-9a-fA-F]{64}$", RegexOptions.Compiled);

    // Downloads the release zip for this architecture, checks its SHA-256, and
    // installs the binary. Arguments are validated before they reach the script.
    internal static string BuildVerifiedInstallScript(string version, string sha256) =>
        "set -eu; export DEBIAN_FRONTEND=noninteractive; " +
        "if ! command -v curl >/dev/null; then " +
        "if command -v apt-get >/dev/null; then apt-get update -qq && apt-get install -y -qq curl ca-certificates; " +
        "else echo 'curl is required to install Bun and apt-get is not available' >&2; exit 1; fi; fi; " +
        "if ! command -v unzip >/dev/null; then " +
        "if command -v apt-get >/dev/null; then apt-get update -qq && apt-get install -y -qq unzip; " +
        "else echo 'unzip is required to install Bun and apt-get is not available' >&2; exit 1; fi; fi; " +
        "case \"$(uname -m)\" in x86_64) arch=x64;; aarch64|arm64) arch=aarch64;; " +
        "*) echo \"Unsupported architecture $(uname -m) for Bun\" >&2; exit 1;; esac; " +
        "tmp=$(mktemp -d); trap 'rm -rf \"$tmp\"' EXIT; " +
        $"curl -fsSL \"https://github.com/oven-sh/bun/releases/download/bun-v{version}/bun-linux-$arch.zip\" -o \"$tmp/bun.zip\"; " +
        $"echo \"{sha256}  $tmp/bun.zip\" | sha256sum -c - >/dev/null 2>&1 || " +
        $"{{ echo \"SHA-256 mismatch for bun-linux-$arch.zip (Bun {version}): expected {sha256}, got $(sha256sum \"$tmp/bun.zip\" | cut -d' ' -f1)\" >&2; exit 1; }}; " +
        "unzip -q -o \"$tmp/bun.zip\" -d \"$tmp\"; " +
        "cp \"$tmp\"/bun-linux-*/bun /usr/local/bin/bun; chmod +x /usr/local/bin/bun; " +
        $"[ \"$(bun --version)\" = \"{version}\" ] || {{ echo \"Bun {version} was not installed (found $(bun --version 2>/dev/null || echo none))\" >&2; exit 1; }}; " +
        "bun --version";
}
