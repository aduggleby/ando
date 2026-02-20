// =============================================================================
// SystemUpdateService.cs
//
// Summary: Optional self-update coordination service for Ando.Server.
//
// Provides update status tracking, periodic image checks, and admin-triggered
// update execution helpers that run through Docker.
//
// Design Decisions:
// - Uses in-memory status for fast UI polling
// - Pulls configured image to compare running vs latest image IDs
// - Executes compose pull/up inside a detached helper container so update can
//   proceed even when the current app container is restarted
// =============================================================================

using System.Diagnostics;
using Ando.Server.Configuration;
using Ando.Server.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Snapshot of server self-update status.
/// </summary>
public record SystemUpdateStatusSnapshot(
    bool Enabled,
    bool IsChecking,
    bool IsUpdateAvailable,
    bool IsUpdateInProgress,
    string? CurrentImageId,
    string? LatestImageId,
    string? CurrentVersion,
    string? LatestVersion,
    DateTime? LastCheckedAtUtc,
    DateTime? LastTriggeredAtUtc,
    string? LastError
);

/// <summary>
/// Service contract for update checks and update triggering.
/// </summary>
public interface ISystemUpdateService
{
    /// <summary>
    /// Gets the current status snapshot.
    /// </summary>
    /// <param name="ensureFresh">When true, performs a refresh first.</param>
    /// <param name="ct">Cancellation token.</param>
    Task<SystemUpdateStatusSnapshot> GetStatusAsync(bool ensureFresh, CancellationToken ct);

    /// <summary>
    /// Refreshes status by checking current and latest image details.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    Task RefreshStatusAsync(CancellationToken ct);

    /// <summary>
    /// Starts an asynchronous update workflow through a detached helper container.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if an update workflow was started; otherwise false.</returns>
    Task<bool> StartUpdateAsync(CancellationToken ct);
}

/// <summary>
/// Docker-based implementation of <see cref="ISystemUpdateService" />.
/// </summary>
public class SystemUpdateService : ISystemUpdateService
{
    private const string DockerSocketMountDestination = "/var/run/docker.sock";
    private const string DockerSocketFallbackSource = "/var/run/docker.sock";
    private readonly SelfUpdateSettings _settings;
    private readonly IHubContext<BuildLogHub> _hubContext;
    private readonly ILogger<SystemUpdateService> _logger;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly SemaphoreSlim _updateLock = new(1, 1);
    private readonly object _statusLock = new();
    private SystemUpdateStatusSnapshot _status;

    /// <summary>
    /// Initializes the self-update service.
    /// </summary>
    public SystemUpdateService(
        IOptions<SelfUpdateSettings> settings,
        IHubContext<BuildLogHub> hubContext,
        ILogger<SystemUpdateService> logger)
    {
        _settings = settings.Value;
        _hubContext = hubContext;
        _logger = logger;
        _status = new SystemUpdateStatusSnapshot(
            _settings.Enabled,
            IsChecking: false,
            IsUpdateAvailable: false,
            IsUpdateInProgress: false,
            CurrentImageId: null,
            LatestImageId: null,
            CurrentVersion: null,
            LatestVersion: null,
            LastCheckedAtUtc: null,
            LastTriggeredAtUtc: null,
            LastError: null);
    }

    /// <inheritdoc />
    public async Task<SystemUpdateStatusSnapshot> GetStatusAsync(bool ensureFresh, CancellationToken ct)
    {
        if (ensureFresh)
        {
            await RefreshStatusAsync(ct);
        }

        lock (_statusLock)
        {
            return _status;
        }
    }

    /// <inheritdoc />
    public async Task RefreshStatusAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
        {
            UpdateStatus(s => s with
            {
                Enabled = false,
                IsChecking = false,
                IsUpdateAvailable = false,
                IsUpdateInProgress = false,
                LastError = null
            });
            return;
        }

        if (!await _refreshLock.WaitAsync(0, ct))
        {
            return;
        }

        try
        {
            UpdateStatus(s => s with { IsChecking = true, Enabled = true, LastError = null });

            var currentImageId = await GetContainerImageIdAsync(_settings.ContainerName, ct);
            if (string.IsNullOrWhiteSpace(currentImageId))
            {
                throw new InvalidOperationException(
                    $"Could not determine running image ID for container '{_settings.ContainerName}'.");
            }

            await RunDockerAsync(["pull", _settings.Image], ct);

            var latestImageId = await GetImageIdAsync(_settings.Image, ct);
            var currentVersion = await GetImageLabelAsync(currentImageId, "org.opencontainers.image.version", ct);
            var latestVersion = await GetImageLabelAsync(_settings.Image, "org.opencontainers.image.version", ct);
            var isUpdateInProgress = await IsUpdateHelperRunningAsync(ct);

            var updateAvailable = !string.IsNullOrWhiteSpace(latestImageId) &&
                                  !string.Equals(currentImageId, latestImageId, StringComparison.Ordinal);

            UpdateStatus(s => s with
            {
                Enabled = true,
                IsChecking = false,
                IsUpdateAvailable = updateAvailable,
                IsUpdateInProgress = isUpdateInProgress,
                CurrentImageId = currentImageId,
                LatestImageId = latestImageId,
                CurrentVersion = currentVersion,
                LatestVersion = latestVersion,
                LastCheckedAtUtc = DateTime.UtcNow,
                LastError = null
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Self-update refresh failed.");
            UpdateStatus(s => s with
            {
                Enabled = true,
                IsChecking = false,
                IsUpdateAvailable = false,
                LastCheckedAtUtc = DateTime.UtcNow,
                LastError = ex.Message
            });
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    /// <inheritdoc />
    public async Task<bool> StartUpdateAsync(CancellationToken ct)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Self-update trigger ignored because SelfUpdate is disabled.");
            return false;
        }

        await _updateLock.WaitAsync(ct);
        try
        {
            if (await IsUpdateHelperRunningAsync(ct))
            {
                _logger.LogInformation("Self-update trigger ignored because update helper is already running.");
                UpdateStatus(s => s with { IsUpdateInProgress = true });
                return false;
            }

            var composeDirectory = Path.GetDirectoryName(_settings.ComposeFilePath);
            if (string.IsNullOrWhiteSpace(composeDirectory))
            {
                throw new InvalidOperationException(
                    $"Invalid compose file path '{_settings.ComposeFilePath}'.");
            }

            var dockerSocketSource = await ResolveDockerSocketSourceAsync(ct);
            if (string.IsNullOrWhiteSpace(dockerSocketSource))
            {
                throw new InvalidOperationException(
                    $"Could not resolve host Docker socket source for '{_settings.ContainerName}'.");
            }

            var helperName = $"ando-self-update-{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}";
            var script =
                $"docker compose -f {_settings.ComposeFilePath} pull {_settings.ServiceName} " +
                $"&& docker compose -f {_settings.ComposeFilePath} up -d {_settings.ServiceName}";

            var runResult = await RunDockerAsync(
                [
                    "run",
                    "--rm",
                    "-d",
                    "--name",
                    helperName,
                    "-v",
                    $"{dockerSocketSource}:{DockerSocketMountDestination}",
                    "-v",
                    $"{composeDirectory}:{composeDirectory}",
                    _settings.HelperImage,
                    "sh",
                    "-lc",
                    script
                ],
                ct);

            _logger.LogInformation(
                "Started self-update helper container {HelperName} (id: {HelperContainerId}).",
                helperName,
                runResult.StdOut.Trim());

            UpdateStatus(s => s with
            {
                Enabled = true,
                IsUpdateInProgress = true,
                LastTriggeredAtUtc = DateTime.UtcNow,
                LastError = null
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start self-update helper.");
            UpdateStatus(s => s with
            {
                Enabled = true,
                IsUpdateInProgress = false,
                LastError = ex.Message
            });
            throw;
        }
        finally
        {
            _updateLock.Release();
        }
    }

    private void UpdateStatus(Func<SystemUpdateStatusSnapshot, SystemUpdateStatusSnapshot> updater)
    {
        SystemUpdateStatusSnapshot updated;
        lock (_statusLock)
        {
            _status = updater(_status);
            updated = _status;
        }

        _ = BroadcastStatusChangedAsync(updated);
    }

    private async Task BroadcastStatusChangedAsync(SystemUpdateStatusSnapshot status)
    {
        try
        {
            await _hubContext.Clients
                .Group(BuildLogHub.GetAdminsGroupName())
                .SendAsync("SystemUpdateStatusChanged", new
                {
                    enabled = status.Enabled,
                    isChecking = status.IsChecking,
                    isUpdateAvailable = status.IsUpdateAvailable,
                    isUpdateInProgress = status.IsUpdateInProgress,
                    currentImageId = status.CurrentImageId,
                    latestImageId = status.LatestImageId,
                    currentVersion = status.CurrentVersion,
                    latestVersion = status.LatestVersion,
                    lastCheckedAtUtc = status.LastCheckedAtUtc,
                    lastTriggeredAtUtc = status.LastTriggeredAtUtc,
                    lastError = status.LastError
                });
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed broadcasting system update status change.");
        }
    }

    private async Task<string?> GetContainerImageIdAsync(string containerName, CancellationToken ct)
    {
        var result = await RunDockerAsync(
            ["inspect", containerName, "--format", "{{.Image}}"],
            ct,
            throwOnError: false);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
    }

    private async Task<string?> GetImageIdAsync(string imageReference, CancellationToken ct)
    {
        var result = await RunDockerAsync(
            ["image", "inspect", imageReference, "--format", "{{.Id}}"],
            ct,
            throwOnError: false);
        return result.ExitCode == 0 ? result.StdOut.Trim() : null;
    }

    private async Task<string?> GetImageLabelAsync(string imageReference, string label, CancellationToken ct)
    {
        var format = $"{{{{index .Config.Labels \"{label}\"}}}}";
        var result = await RunDockerAsync(
            ["image", "inspect", imageReference, "--format", format],
            ct,
            throwOnError: false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var value = result.StdOut.Trim();
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private async Task<string?> ResolveDockerSocketSourceAsync(CancellationToken ct)
    {
        // Try the configured container first.
        var fromConfiguredContainer = await InspectMountSourceAsync(_settings.ContainerName, DockerSocketMountDestination, ct);
        if (!string.IsNullOrWhiteSpace(fromConfiguredContainer))
        {
            return fromConfiguredContainer;
        }

        // Fall back to this container ID/name from HOSTNAME when available.
        var hostName = Environment.GetEnvironmentVariable("HOSTNAME");
        if (!string.IsNullOrWhiteSpace(hostName) &&
            !string.Equals(hostName, _settings.ContainerName, StringComparison.OrdinalIgnoreCase))
        {
            var fromHostName = await InspectMountSourceAsync(hostName, DockerSocketMountDestination, ct);
            if (!string.IsNullOrWhiteSpace(fromHostName))
            {
                return fromHostName;
            }
        }

        // Fall back to the currently running compose service container.
        var serviceContainerName = await GetRunningServiceContainerNameAsync(ct);
        if (!string.IsNullOrWhiteSpace(serviceContainerName) &&
            !string.Equals(serviceContainerName, _settings.ContainerName, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(serviceContainerName, hostName, StringComparison.OrdinalIgnoreCase))
        {
            var fromServiceContainer = await InspectMountSourceAsync(serviceContainerName, DockerSocketMountDestination, ct);
            if (!string.IsNullOrWhiteSpace(fromServiceContainer))
            {
                return fromServiceContainer;
            }
        }

        // If DOCKER_HOST is a unix socket, use its path directly.
        var dockerHost = Environment.GetEnvironmentVariable("DOCKER_HOST");
        if (!string.IsNullOrWhiteSpace(dockerHost) &&
            Uri.TryCreate(dockerHost, UriKind.Absolute, out var dockerHostUri) &&
            string.Equals(dockerHostUri.Scheme, "unix", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(dockerHostUri.AbsolutePath))
        {
            return dockerHostUri.AbsolutePath;
        }

        _logger.LogWarning(
            "Could not resolve socket mount source via inspect for container '{ContainerName}'. Falling back to {FallbackSource}.",
            _settings.ContainerName,
            DockerSocketFallbackSource);
        return DockerSocketFallbackSource;
    }

    private async Task<string?> InspectMountSourceAsync(string containerNameOrId, string destination, CancellationToken ct)
    {
        var format = $"{{{{range .Mounts}}}}{{{{if eq .Destination \\\"{destination}\\\"}}}}{{{{.Source}}}}{{{{end}}}}{{{{end}}}}";
        var result = await RunDockerAsync(
            ["inspect", containerNameOrId, "--format", format],
            ct,
            throwOnError: false);

        if (result.ExitCode != 0)
        {
            _logger.LogDebug(
                "docker inspect failed while resolving socket source for {Container}: {Error}",
                containerNameOrId,
                result.StdErr.Trim());
            return null;
        }

        var source = result.StdOut.Trim();
        return string.IsNullOrWhiteSpace(source) ? null : source;
    }

    private async Task<string?> GetRunningServiceContainerNameAsync(CancellationToken ct)
    {
        var result = await RunDockerAsync(
            ["ps", "--filter", $"label=com.docker.compose.service={_settings.ServiceName}", "--format", "{{.Names}}"],
            ct,
            throwOnError: false);

        if (result.ExitCode != 0)
        {
            return null;
        }

        var firstLine = result.StdOut
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault();
        return string.IsNullOrWhiteSpace(firstLine) ? null : firstLine.Trim();
    }

    private async Task<bool> IsUpdateHelperRunningAsync(CancellationToken ct)
    {
        var result = await RunDockerAsync(
            ["ps", "--filter", "name=ando-self-update-", "--format", "{{.ID}}"],
            ct,
            throwOnError: false);

        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.StdOut);
    }

    private async Task<ProcessResult> RunDockerAsync(
        IReadOnlyList<string> args,
        CancellationToken ct,
        bool throwOnError = true)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start docker process.");

        var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        var result = new ProcessResult(process.ExitCode, stdout, stderr);

        if (throwOnError && result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"docker {string.Join(' ', args)} failed with exit code {result.ExitCode}: {result.StdErr}");
        }

        return result;
    }

    private sealed record ProcessResult(int ExitCode, string StdOut, string StdErr);
}
