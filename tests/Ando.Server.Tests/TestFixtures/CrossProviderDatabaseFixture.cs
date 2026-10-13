// =============================================================================
// CrossProviderDatabaseFixture.cs
//
// Summary: Starts one SQL Server and one PostgreSQL container for tests that
// need both providers at once (data export/import round trips).
//
// A test class belongs to exactly one xUnit collection, and the existing
// SqlServer/PostgreSql collections own the shared fixed-name containers. A
// second fixture instance in shared mode would "docker rm -f" a container that
// the other collection is using in parallel, so this fixture always runs its
// own containers in isolated mode (unique names, random ports, removed at the
// end of the run), regardless of ANDO_TEST_DB_CONTAINERS.
//
// Design Decisions:
// - Starts containers only when BOTH providers are selected by
//   ANDO_TEST_DB_PROVIDERS; otherwise the cross-provider tests are skipped
// - Containers start in parallel to keep the collection's startup short
// =============================================================================

using Ando.Server.Configuration;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// xUnit fixture holding private isolated SQL Server and PostgreSQL containers.
/// </summary>
public sealed class CrossProviderDatabaseFixture : IAsyncLifetime
{
    /// <summary>
    /// The SQL Server container fixture (isolated mode).
    /// </summary>
    public SqlServerFixture SqlServer { get; } = new(DatabaseContainerMode.Isolated);

    /// <summary>
    /// The PostgreSQL container fixture (isolated mode).
    /// </summary>
    public PostgreSqlFixture PostgreSql { get; } = new(DatabaseContainerMode.Isolated);

    /// <summary>
    /// True when both providers are selected, so both containers run.
    /// </summary>
    public static bool BothSelected =>
        DatabaseTestProviders.IsSelected(DatabaseProvider.SqlServer)
        && DatabaseTestProviders.IsSelected(DatabaseProvider.PostgreSql);

    /// <summary>
    /// Skip reason when a provider is deselected.
    /// </summary>
    public static string SkipReason =>
        $"Cross-provider tests need both providers selected by {DatabaseTestProviders.EnvironmentVariable} " +
        $"(current: '{Environment.GetEnvironmentVariable(DatabaseTestProviders.EnvironmentVariable)}').";

    /// <summary>
    /// Returns the fixture for <paramref name="provider"/>.
    /// </summary>
    /// <param name="provider">The provider.</param>
    /// <returns>The container fixture.</returns>
    public IDatabaseFixture For(DatabaseProvider provider) =>
        provider == DatabaseProvider.SqlServer ? SqlServer : PostgreSql;

    /// <summary>
    /// Starts both containers when both providers are selected.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (!BothSelected)
        {
            return;
        }

        await Task.WhenAll(SqlServer.InitializeAsync(), PostgreSql.InitializeAsync());
    }

    /// <summary>
    /// Stops and removes both containers.
    /// </summary>
    public async Task DisposeAsync()
    {
        await Task.WhenAll(SqlServer.DisposeAsync(), PostgreSql.DisposeAsync());
    }
}

/// <summary>
/// xUnit collection for tests that use both database providers.
/// </summary>
[CollectionDefinition(Name)]
public class CrossProviderCollection : ICollectionFixture<CrossProviderDatabaseFixture>
{
    /// <summary>
    /// Collection name.
    /// </summary>
    public const string Name = "CrossProvider";
}
