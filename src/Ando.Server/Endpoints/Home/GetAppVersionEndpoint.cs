// =============================================================================
// GetAppVersionEndpoint.cs
//
// Summary: Returns the running Ando.Server version for UI display.
// =============================================================================

using Ando.Server.Contracts.Home;
using FastEndpoints;

namespace Ando.Server.Endpoints.Home;

/// <summary>
/// GET /api/app/version - Returns the server version string.
/// </summary>
public class GetAppVersionEndpoint : EndpointWithoutRequest<AppVersionResponse>
{
    public override void Configure()
    {
        Get("/app/version");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var version = typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown";
        await SendAsync(new AppVersionResponse(version), cancellation: ct);
    }
}
