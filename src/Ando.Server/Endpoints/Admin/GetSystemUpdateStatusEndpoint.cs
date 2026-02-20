// =============================================================================
// GetSystemUpdateStatusEndpoint.cs
//
// Summary: Admin endpoint for server self-update status.
//
// Returns cached + periodically refreshed status for optional self-update
// functionality, including whether a newer image is available.
// =============================================================================

using Ando.Server.Contracts.Admin;
using Ando.Server.Models;
using Ando.Server.Services;
using FastEndpoints;

namespace Ando.Server.Endpoints.Admin;

/// <summary>
/// GET /api/admin/system-update - Get server self-update status.
/// </summary>
public class GetSystemUpdateStatusEndpoint : EndpointWithoutRequest<SystemUpdateStatusResponse>
{
    private readonly ISystemUpdateService _systemUpdateService;

    /// <summary>
    /// Initializes the endpoint.
    /// </summary>
    public GetSystemUpdateStatusEndpoint(ISystemUpdateService systemUpdateService)
    {
        _systemUpdateService = systemUpdateService;
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Get("/admin/system-update");
        Roles(UserRoles.Admin);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken ct)
    {
        var forceRefresh = Query<bool>("refresh", isRequired: false);
        var status = await _systemUpdateService.GetStatusAsync(forceRefresh, ct);

        await SendAsync(new SystemUpdateStatusResponse(
            status.Enabled,
            status.IsChecking,
            status.IsUpdateAvailable,
            status.IsUpdateInProgress,
            status.CurrentImageId,
            status.LatestImageId,
            status.CurrentVersion,
            status.LatestVersion,
            status.LastCheckedAtUtc,
            status.LastTriggeredAtUtc,
            status.LastError
        ), cancellation: ct);
    }
}

