// =============================================================================
// HealthEndpoint.cs
//
// Summary: FastEndpoint for basic health check.
//
// Returns a simple healthy status for load balancer and Docker health checks.
//
// Design Decisions:
// - Always returns 200 if the server is running
// - No authentication required
// =============================================================================

using Ando.Server.Contracts.Home;
using FastEndpoints;

namespace Ando.Server.Endpoints.Home;

/// <summary>
/// GET /api/health - Basic health check.
/// </summary>
public class HealthEndpoint : EndpointWithoutRequest<HealthResponse>
{
    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        await SendAsync(new HealthResponse("healthy", DateTime.UtcNow), cancellation: ct);
    }
}
