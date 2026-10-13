// =============================================================================
// HangfireStorageTests_PostgreSql.cs
//
// Summary: Runs the real-storage Hangfire worker tests on PostgreSQL storage
// (Hangfire.PostgreSql with UseSlidingInvisibilityTimeout) and checks that
// the storage installs its own "hangfire" schema.
//
// Design Decisions:
// - Shared test bodies live in HangfireStorageTestsBase; only the schema check
//   is PostgreSQL-specific (SQL Server's storage uses its fixed HangFire schema)
// =============================================================================

using Ando.Server.Tests.TestFixtures;
using Npgsql;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Hangfire real-storage tests against PostgreSQL.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public class HangfireStorageTests_PostgreSql : HangfireStorageTestsBase
{
    /// <summary>
    /// Creates the tests for the shared PostgreSQL container.
    /// </summary>
    /// <param name="fixture">PostgreSQL container fixture.</param>
    public HangfireStorageTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture)
    {
    }

    [SkippableFact]
    public async Task Storage_CreatesHangfireSchema()
    {
        CreateStorage();

        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'hangfire' AND table_name = 'job')",
            connection);
        ((bool)(await command.ExecuteScalarAsync())!).ShouldBeTrue();
    }
}
