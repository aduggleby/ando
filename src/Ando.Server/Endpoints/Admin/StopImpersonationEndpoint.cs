// =============================================================================
// StopImpersonationEndpoint.cs
//
// Summary: FastEndpoint for stopping user impersonation.
//
// Returns to the admin's original account.
//
// Design Decisions:
// - Allows anonymous access (user won't have admin role while impersonating)
// - Only works if currently impersonating
// =============================================================================

using Ando.Server.Contracts.Admin;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// POST /api/admin/stop-impersonation - Stop impersonating.
/// </summary>
public class StopImpersonationEndpoint : EndpointWithoutRequest<StopImpersonationResponse>
{
    private readonly IImpersonationService _impersonationService;
    private readonly ILogger<StopImpersonationEndpoint> _logger;

    public StopImpersonationEndpoint(
        IImpersonationService impersonationService,
        ILogger<StopImpersonationEndpoint> logger)
    {
        _impersonationService = impersonationService;
        _logger = logger;
    }

    public override void Configure()
    {
        Post("/admin/stop-impersonation");
        AllowAnonymous(); // Allow because user won't have admin role while impersonating
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        if (!_impersonationService.IsImpersonating)
        {
            await SendAsync(new StopImpersonationResponse(true), cancellation: ct);
            return;
        }

        await _impersonationService.StopImpersonationAsync();
        _logger.LogInformation("Impersonation ended");

        await SendAsync(new StopImpersonationResponse(true), cancellation: ct);
    }
}
