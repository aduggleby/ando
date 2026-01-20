// =============================================================================
// DotnetSdkEnsurer.cs
//
// Summary: Ensures .NET SDK is installed before executing dotnet commands.
//
// This class provides automatic .NET SDK installation at execution time.
// When a build script uses Dotnet.Build() or similar operations, the ensurer
// automatically checks if the required SDK is installed and installs it if needed.
//
// The ensurer follows the same pattern as DotnetTool.EnsureInstalledAsync():
// - Tracks installation state to avoid redundant installs
// - Supports manual install flag to disable auto-install when user calls SdkInstall()
// - Checks for existing installation (warm container optimization)
//
// Design Decisions:
// - Lazy installation at execution time, not registration time
// - Cached installation state per build session
// - Manual install flag respects user's explicit version choice
// - Uses Debug logging for checks, Info logging for actual installations
// =============================================================================

using Ando.Execution;
using Ando.Logging;

namespace Ando.Utilities;

/// <summary>
/// Ensures .NET SDK is installed before command execution.
/// Automatically installs the latest SDK version if not present.
/// </summary>
public class DotnetSdkEnsurer
{
    private readonly VersionResolver _versionResolver;
    private readonly Func<ICommandExecutor> _executorFactory;
    private readonly IMessageLogger _logger;

    private bool _installed;
    private bool _manualInstallCalled;

    public DotnetSdkEnsurer(
        VersionResolver versionResolver,
        Func<ICommandExecutor> executorFactory,
        IMessageLogger logger)
    {
        _versionResolver = versionResolver;
        _executorFactory = executorFactory;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the .NET SDK is installed.
    /// Skips installation if already installed or if manual install was called.
    /// </summary>
    public async Task EnsureInstalledAsync()
    {
        // Skip if already ensured this session or if user called SdkInstall() manually.
        if (_installed || _manualInstallCalled)
        {
            return;
        }

        var version = await _versionResolver.GetLatestDotnetSdkVersionAsync();

        // Check if the correct version is already installed (warm container optimization).
        var checkResult = await _executorFactory().ExecuteAsync("bash",
            ["-c", $"command -v dotnet >/dev/null && dotnet --version | grep -q '^{version}\\.'"],
            new CommandOptions());

        if (checkResult.Success)
        {
            _logger.Debug($".NET SDK {version} already installed");
            _installed = true;
            return;
        }

        // Install the SDK.
        _logger.Info($"Installing .NET SDK {version}...");

        // Use the same installation script as Dotnet.SdkInstall().
        // Installs dependencies, downloads install script, and creates symlink.
        var installScript =
            $"apt-get update && apt-get install -y curl ca-certificates libicu70 && " +
            $"curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel {version} && " +
            "ln -sf $HOME/.dotnet/dotnet /usr/local/bin/dotnet";

        var installResult = await _executorFactory().ExecuteAsync("bash",
            ["-c", installScript],
            new CommandOptions());

        if (installResult.Success)
        {
            _logger.Debug($".NET SDK {version} installed successfully");
        }
        else
        {
            _logger.Warning($".NET SDK {version} installation may have failed: {installResult.Error}");
        }

        _installed = true;
    }

    /// <summary>
    /// Marks that a manual SDK install was called (via Dotnet.SdkInstall()).
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
