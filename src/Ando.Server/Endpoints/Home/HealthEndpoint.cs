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
// - Reports the configured database provider so deployments and the E2E
//   global setup can verify which provider the server actually runs on
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Contracts.Home;
using Ando.Server.Data;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Endpoints.Home;

/// <summary>
/// GET /api/health - Basic health check.
/// </summary>
public class HealthEndpoint : EndpointWithoutRequest<HealthResponse>
{
    private readonly AndoDbContext _db;
    private readonly string _databaseProvider;

    /// <summary>
    /// Initializes endpoint dependencies.
    /// </summary>
    /// <param name="db">Application database context.</param>
    /// <param name="databaseSettings">Database settings; defaults to SqlServer when unbound.</param>
    public HealthEndpoint(AndoDbContext db, IOptions<DatabaseSettings> databaseSettings)
    {
        _db = db;
        _databaseProvider = databaseSettings.Value.Provider.ToString();
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
                await Send.ResponseAsync(new HealthResponse("unhealthy", DateTime.UtcNow, _databaseProvider), 503, ct);
                return;
            }

            // Run a tiny query to validate command execution, not just TCP reachability.
            await _db.Projects.AsNoTracking().Select(p => p.Id).FirstOrDefaultAsync(ct);
            await Send.OkAsync(new HealthResponse("healthy", DateTime.UtcNow, _databaseProvider), cancellation: ct);
        }
        catch
        {
            await Send.ResponseAsync(new HealthResponse("unhealthy", DateTime.UtcNow, _databaseProvider), 503, ct);
        }
    }
}
