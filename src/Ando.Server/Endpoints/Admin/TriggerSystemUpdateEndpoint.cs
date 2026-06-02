// =============================================================================
// TriggerSystemUpdateEndpoint.cs
//
// Summary: Admin endpoint for triggering an app self-update.
//
// Queues a Hangfire background job that starts a detached helper container to
// run compose pull/up for the configured Ando.Server service.
// =============================================================================

using System.Security.Claims;
using Ando.Server.Contracts.Admin;
using Ando.Server.Jobs;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;
using Hangfire;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// POST /api/admin/system-update - Trigger server self-update.
/// </summary>
public class TriggerSystemUpdateEndpoint : EndpointWithoutRequest<TriggerSystemUpdateResponse>
{
    private readonly IBackgroundJobClient _jobClient;
    private readonly IAuditLogger _auditLogger;
    private readonly ILogger<TriggerSystemUpdateEndpoint> _logger;

    /// <summary>
    /// Initializes the endpoint.
    /// </summary>
    public TriggerSystemUpdateEndpoint(
        IBackgroundJobClient jobClient,
        IAuditLogger auditLogger,
        ILogger<TriggerSystemUpdateEndpoint> logger)
    {
        _jobClient = jobClient;
        _auditLogger = auditLogger;
        _logger = logger;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Post("/admin/system-update");
        Roles(UserRoles.Admin);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken ct)
    {
        var adminId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0");
        var adminEmail = User.FindFirst(ClaimTypes.Email)?.Value;

        try
        {
            var jobId = _jobClient.Enqueue<ApplySystemUpdateJob>(
                job => job.ExecuteAsync(CancellationToken.None));

            _auditLogger.LogAdminAction(
                "SystemUpdateQueued",
                "Admin queued server self-update.",
                adminId,
                adminEmail,
                metadata: new Dictionary<string, object>
                {
                    ["jobId"] = jobId
                });

            await SendAsync(
                new TriggerSystemUpdateResponse(
                    true,
                    "Update job queued. The server will pull and restart if a new image is available.",
                    jobId),
                cancellation: ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to queue system update job.");
            _auditLogger.LogAdminAction(
                "SystemUpdateQueueFailed",
                "Admin attempted to queue server self-update but it failed.",
                adminId,
                adminEmail,
                metadata: new Dictionary<string, object> { ["error"] = ex.Message },
                success: false);

            await SendAsync(
                new TriggerSystemUpdateResponse(
                    false,
                    "Failed to queue update job."),
                500,
                ct);
        }
    }
}

