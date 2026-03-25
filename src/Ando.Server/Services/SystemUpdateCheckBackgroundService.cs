// =============================================================================
// SystemUpdateCheckBackgroundService.cs
//
// Summary: Periodic background self-update checker.
//
// Executes update checks on a timer so admin UI can display current status
// without triggering expensive image pulls on every request.
// =============================================================================

using Ando.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Ando.Server.Services;

/// <summary>
/// Hosted service that periodically refreshes self-update status.
/// </summary>
public class SystemUpdateCheckBackgroundService : BackgroundService
{
    private readonly SelfUpdateSettings _settings;
    private readonly ISystemUpdateService _systemUpdateService;
    private readonly ILogger<SystemUpdateCheckBackgroundService> _logger;

    /// <summary>
    /// Initializes the background checker.
    /// </summary>
    public SystemUpdateCheckBackgroundService(
        IOptions<SelfUpdateSettings> settings,
        ISystemUpdateService systemUpdateService,
        ILogger<SystemUpdateCheckBackgroundService> logger)
    {
        _settings = settings.Value;
        _systemUpdateService = systemUpdateService;
        _logger = logger;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_settings.Enabled)
        {
            _logger.LogInformation("Self-update background checker is disabled.");
            return;
        }

        var intervalMinutes = Math.Max(1, _settings.CheckIntervalMinutes);
        var interval = TimeSpan.FromMinutes(intervalMinutes);

        _logger.LogInformation(
            "Self-update background checker started. Interval={IntervalMinutes} minute(s).",
            intervalMinutes);

        // Prime status quickly after startup.
        await SafeRefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await SafeRefreshAsync(stoppingToken);
        }
    }

    private async Task SafeRefreshAsync(CancellationToken ct)
    {
        try
        {
            await _systemUpdateService.RefreshStatusAsync(ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Self-update background refresh failed.");
        }
    }
}

