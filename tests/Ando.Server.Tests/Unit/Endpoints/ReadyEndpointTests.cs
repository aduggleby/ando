// =============================================================================
// ReadyEndpointTests.cs
//
// Summary: Unit tests for the /ready probe's unready path.
//
// Design Decisions:
// - Uses an Npgsql context pointed at a closed local port with a 1 s timeout,
//   so the database is unreachable without Docker; the 200 path is covered by
//   ProbeEndpointTests over HTTP
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Endpoints.Infrastructure;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Tests.Unit.Endpoints;

/// <summary>
/// Tests for <see cref="ReadyEndpoint"/>.
/// </summary>
public class ReadyEndpointTests
{
    [Fact]
    public async Task HandleAsync_DatabaseUnreachable_Returns503Unready()
    {
        await using var db = new PostgreSqlAndoDbContext(
            new DbContextOptionsBuilder<PostgreSqlAndoDbContext>()
                .UseNpgsql("Host=127.0.0.1;Port=1;Database=ando;Username=x;Password=x;Timeout=1")
                .Options);
        var endpoint = Factory.Create<ReadyEndpoint>(
            db, Options.Create(new DatabaseSettings { Provider = DatabaseProvider.PostgreSql }));

        await endpoint.HandleAsync(CancellationToken.None);

        endpoint.HttpContext.Response.StatusCode.ShouldBe(503);
        var body = System.Text.Json.JsonSerializer.SerializeToElement(endpoint.Response);
        body.GetProperty("status").GetString().ShouldBe("unready");
        body.GetProperty("database").GetString().ShouldBe("PostgreSql");
    }
}
