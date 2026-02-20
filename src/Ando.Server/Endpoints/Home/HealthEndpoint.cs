// =============================================================================
// HealthEndpoint.cs
//
// Summary: FastEndpoint for basic health check.
//
// Returns readiness status based on live database connectivity.
//
// Design Decisions:
// - Returns 200 only when the API can execute a database query
// - No authentication required
// =============================================================================

using Ando.Server.Contracts.Home;
using Ando.Server.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Endpoints.Home;

/// <summary>
/// GET /api/health - Basic health check.
/// </summary>
public class HealthEndpoint : EndpointWithoutRequest<HealthResponse>
{
    private readonly AndoDbContext _db;

    /// <summary>
    /// Initializes endpoint dependencies.
    /// </summary>
    /// <param name="db">Application database context.</param>
    public HealthEndpoint(AndoDbContext db)
    {
        _db = db;
    }

    public override void Configure()
    {
        Get("/health");
        AllowAnonymous();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        try
        {
            var canConnect = await _db.Database.CanConnectAsync(ct);
            if (!canConnect)
            {
                await SendAsync(new HealthResponse("unhealthy", DateTime.UtcNow), 503, ct);
                return;
            }

            // Run a tiny query to validate command execution, not just TCP reachability.
            await _db.Projects.AsNoTracking().Select(p => p.Id).FirstOrDefaultAsync(ct);
            await SendAsync(new HealthResponse("healthy", DateTime.UtcNow), cancellation: ct);
        }
        catch
        {
            await SendAsync(new HealthResponse("unhealthy", DateTime.UtcNow), 503, ct);
        }
    }
}
