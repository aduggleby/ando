// =============================================================================
// NodeSdkEnsurer.cs
//
// Summary: Ensures Node.js is installed before executing npm commands.
//
// This class provides automatic Node.js installation at execution time.
// When a build script uses Npm.Ci() or similar operations, the ensurer
// automatically checks if Node.js is installed and installs it if needed.
//
// The ensurer follows the same pattern as DotnetSdkEnsurer:
// - Tracks installation state to avoid redundant installs
// - Supports manual install flag to disable auto-install when user calls Node.Install()
// - Checks for existing installation (warm container optimization)
//
// Design Decisions:
// - Lazy installation at execution time, not registration time
// - Cached installation state per build session
// - Manual install flag respects user's explicit version choice
// - Uses NodeSource for reliable Node.js installation on Ubuntu/Debian
// =============================================================================

using Ando.Execution;
using Ando.Logging;

namespace Ando.Utilities;

/// <summary>
/// Ensures Node.js is installed before command execution.
/// Automatically installs the latest LTS version if not present.
/// </summary>
public class NodeSdkEnsurer
{
    private readonly VersionResolver _versionResolver;
    private readonly Func<ICommandExecutor> _executorFactory;
    private readonly IMessageLogger _logger;

    private bool _installed;
    private bool _manualInstallCalled;

    public NodeSdkEnsurer(
        VersionResolver versionResolver,
        Func<ICommandExecutor> executorFactory,
        IMessageLogger logger)
    {
        _versionResolver = versionResolver;
        _executorFactory = executorFactory;
        _logger = logger;
    }

    /// <summary>
    /// Ensures Node.js is installed.
    /// Skips installation if already installed or if manual install was called.
    /// </summary>
    public async Task EnsureInstalledAsync()
    {
        // Skip if already ensured this session or if user called Node.Install() manually.
        if (_installed || _manualInstallCalled)
        {
            return;
        }

        var version = await _versionResolver.GetLatestNodeLtsVersionAsync();

        // Check if the correct version is already installed (warm container optimization).
        var checkResult = await _executorFactory().ExecuteAsync("bash",
            ["-c", $"command -v node >/dev/null && node -v | grep -q '^v{version}\\.'"],
            new CommandOptions());

        if (checkResult.Success)
        {
            _logger.Debug($"Node.js v{version} already installed");
            _installed = true;
            return;
        }

        // Install Node.js.
        _logger.Info($"Installing Node.js v{version}...");

        // Use NodeSource for reliable installation (same as Node.Install()).
        var installScript =
            $"apt-get update && apt-get install -y curl ca-certificates && " +
            $"curl -fsSL https://deb.nodesource.com/setup_{version}.x | bash - && " +
            $"apt-get install -y nodejs";

        var installResult = await _executorFactory().ExecuteAsync("bash",
            ["-c", installScript],
            new CommandOptions());

        if (installResult.Success)
        {
            _logger.Debug($"Node.js v{version} installed successfully");
        }
        else
        {
            _logger.Warning($"Node.js v{version} installation may have failed: {installResult.Error}");
        }

        _installed = true;
    }

    /// <summary>
    /// Marks that a manual Node install was called (via Node.Install()).
    /// This disables automatic installation since the user wants a specific version.
    /// </summary>
    public void MarkManualInstallCalled()
    {
        _manualInstallCalled = true;
    }

    /// <summary>
    /// Indicates whether the ensurer has already ensured installation.
    /// </summary>
    public bool IsInstalled => _installed;

    /// <summary>
    /// Indicates whether manual install was called.
    /// </summary>
    public bool ManualInstallCalled => _manualInstallCalled;

    // For testing: reset state.
    internal void Reset()
    {
        _installed = false;
        _manualInstallCalled = false;
    }
}
