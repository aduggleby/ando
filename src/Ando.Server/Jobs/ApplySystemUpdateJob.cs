// =============================================================================
// ApplySystemUpdateJob.cs
//
// Summary: Hangfire job for applying server self-updates.
//
// Triggered by admin action. Delegates update execution to SystemUpdateService,
// which starts a detached helper container that performs compose pull/up.
// =============================================================================

using Hangfire;
using Ando.Server.Services;

namespace Ando.Server.Jobs;

/// <summary>
/// Hangfire job that starts the server update workflow.
/// </summary>
public class ApplySystemUpdateJob
{
    private readonly ISystemUpdateService _systemUpdateService;
    private readonly ILogger<ApplySystemUpdateJob> _logger;

    /// <summary>
    /// Initializes the job.
    /// </summary>
    public ApplySystemUpdateJob(
        ISystemUpdateService systemUpdateService,
        ILogger<ApplySystemUpdateJob> logger)
    {
        _systemUpdateService = systemUpdateService;
        _logger = logger;
    }

    /// <summary>
    /// Starts the self-update workflow.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [Queue("default")]
    [AutomaticRetry(Attempts = 0)]
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var started = await _systemUpdateService.StartUpdateAsync(cancellationToken);
        if (!started)
        {
            _logger.LogInformation("Self-update job executed but no new update workflow was started.");
        }
    }
}

