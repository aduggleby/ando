// =============================================================================
// IDatabaseFixture.cs
//
// Summary: Common surface of the container fixtures (SQL Server, PostgreSQL)
// so provider-agnostic test bases can run against either.
//
// Abstract test bases (webhook, migration schema, Hangfire storage) take an
// IDatabaseFixture and use Provider to decide whether to skip and which EF
// provider / Hangfire storage to build.
//
// Design Decisions:
// - IsAvailable is false when the provider is not selected by
//   ANDO_TEST_DB_PROVIDERS; the container is then never started and
//   ConnectionString must not be read
// =============================================================================

using Ando.Server.Configuration;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// A container-backed database fixture for one provider.
/// </summary>
public interface IDatabaseFixture
{
    /// <summary>
    /// The provider this fixture's container runs.
    /// </summary>
    DatabaseProvider Provider { get; }

    /// <summary>
    /// True when the provider is selected and the container was started.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Connection string to the container's default database.
    /// </summary>
    string ConnectionString { get; }

    /// <summary>
    /// Connection string to the container pointed at <paramref name="databaseName"/>.
    /// </summary>
    /// <param name="databaseName">Database name to target.</param>
    /// <returns>The connection string.</returns>
    string GetConnectionString(string databaseName);
}
