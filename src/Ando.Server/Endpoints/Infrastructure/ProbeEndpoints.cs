// =============================================================================
// ProbeEndpoints.cs
//
// Summary: Liveness and readiness probes for orchestrators and load balancers.
//
// GET /live answers as long as the process serves HTTP; GET /ready also checks
// that the database accepts connections. /health (database query plus provider)
// is unchanged and stays the Docker healthcheck target.
//
// Design Decisions:
// - /live has no dependencies, so a database outage never restarts the container
// - /ready uses Database.CanConnectAsync only (no table query) and treats an
//   exception the same as "cannot connect": 503, so traffic is held back
// - Both are anonymous and outside the /api prefix, like /health; operators
//   should not expose them publicly (see the reverse proxy example in the docs)
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Data;
using FastEndpoints;
using Microsoft.Extensions.Options;

namespace Ando.Server.Endpoints.Infrastructure;

/// <summary>
/// GET /live - Liveness probe without dependencies.
/// </summary>
public class LiveEndpoint : EndpointWithoutRequest<object>
{
    /// <inheritdoc />
    public override void Configure()
    {
        Get("/live");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken ct)
    {
        await Send.OkAsync(new { status = "live" }, cancellation: ct);
    }
}

/// <summary>
/// GET /ready - Readiness probe: 200 when the database accepts connections, else 503.
/// </summary>
public class ReadyEndpoint : EndpointWithoutRequest<object>
{
    private readonly AndoDbContext _db;
    private readonly string _databaseProvider;

    /// <summary>
    /// Initializes endpoint dependencies.
    /// </summary>
    /// <param name="db">Application database context.</param>
    /// <param name="databaseSettings">Database settings; the provider name is reported.</param>
    public ReadyEndpoint(AndoDbContext db, IOptions<DatabaseSettings> databaseSettings)
    {
        _db = db;
        _databaseProvider = databaseSettings.Value.Provider.ToString();
    }

    /// <inheritdoc />
    public override void Configure()
    {
        Get("/ready");
        AllowAnonymous();
        RoutePrefixOverride(string.Empty);
    }

    /// <inheritdoc />
    public override async Task HandleAsync(CancellationToken ct)
    {
        string? error;
        try
        {
            error = await _db.Database.CanConnectAsync(ct) ? null : "Cannot connect to database";
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            error = ex.Message;
        }

        if (error == null)
        {
            await Send.OkAsync(new { status = "ready", database = _databaseProvider }, cancellation: ct);
            return;
        }

        await Send.ResponseAsync(new { status = "unready", database = _databaseProvider, error }, 503, ct);
    }
}
