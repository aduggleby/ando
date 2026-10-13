// =============================================================================
// PostgreSqlFixture.cs
//
// Summary: Manages a PostgreSQL container for Ando.Server integration tests.
//
// Uses Testcontainers to spin up a real PostgreSQL instance in Docker. Mirrors
// SqlServerFixture: the container is shared across all tests in the collection,
// and each test class gets its own database for isolation.
//
// Design Decisions:
// - Uses postgres:17-alpine to match the compose deployment
// - Container ownership follows ANDO_TEST_DB_CONTAINERS (see
//   DatabaseTestContainerMode):
//   - shared (default): dedicated fixed name and host port 17135 (ANDO range,
//     clear of the dev 17134 and E2E databases), recreated for each test run,
//     left running afterwards for data inspection
//   - isolated: unique name, random host port, removed when the run ends, so
//     concurrent runs on one Docker daemon cannot remove each other's container
// - When the tests run inside a container, the host is rewritten to
//   the container's own IP and port over a shared Docker network (TestcontainersEnvironment)
// - Implements IAsyncLifetime for proper async setup/teardown
// - The internal constructor forces a container mode, used by
//   CrossProviderDatabaseFixture to start private isolated containers
// - Starts the container only when the provider is selected by
//   ANDO_TEST_DB_PROVIDERS (see DatabaseTestProviders)
// =============================================================================

using System.Diagnostics;
using Ando.Server.Configuration;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// xUnit fixture that manages a PostgreSQL container for integration tests.
/// </summary>
public class PostgreSqlFixture : IAsyncLifetime, IDatabaseFixture
{
    private const string SharedContainerName = "ando-server-tests-postgres";
    private const string Username = "ando";
    private const string Password = "Test@Password123!";
    private const int SharedPort = 17135; // Fixed host port in shared mode
    private const int ContainerPort = 5432;

    private PostgreSqlContainer? _container;
    private readonly DatabaseContainerMode? _modeOverride;
    private DatabaseContainerMode _mode;
    private string? _connectionString;

    /// <summary>
    /// Creates the fixture; the container mode follows ANDO_TEST_DB_CONTAINERS.
    /// </summary>
    public PostgreSqlFixture() : this(null)
    {
    }

    /// <summary>
    /// Creates the fixture with a fixed container mode. Fixtures owned by a second
    /// collection (CrossProviderDatabaseFixture) pass Isolated so they never
    /// replace the shared fixed-name container another collection is using.
    /// </summary>
    /// <param name="modeOverride">Mode to use instead of ANDO_TEST_DB_CONTAINERS, or null.</param>
    internal PostgreSqlFixture(DatabaseContainerMode? modeOverride)
    {
        _modeOverride = modeOverride;
    }

    /// <inheritdoc />
    public DatabaseProvider Provider => DatabaseProvider.PostgreSql;

    /// <inheritdoc />
    public bool IsAvailable => _connectionString != null;

    /// <summary>
    /// Gets the connection string for the PostgreSQL container.
    /// </summary>
    public string ConnectionString => _connectionString
        ?? throw new InvalidOperationException(
            DatabaseTestProviders.IsSelected(Provider)
                ? "Container not initialized"
                : DatabaseTestProviders.SkipReason(Provider));

    /// <summary>
    /// Starts a fresh PostgreSQL container when PostgreSQL is selected.
    /// </summary>
    public async Task InitializeAsync()
    {
        // Only start the container when PostgreSql is selected by
        // ANDO_TEST_DB_PROVIDERS; tests of a deselected provider are skipped.
        if (!DatabaseTestProviders.IsSelected(Provider))
        {
            return;
        }

        _mode = _modeOverride ?? DatabaseTestContainerMode.GetCurrent();
        var builder = new PostgreSqlBuilder("postgres:17-alpine")
            .WithUsername(Username)
            .WithPassword(Password);

        if (_mode == DatabaseContainerMode.Shared)
        {
            // Always recreate the container to avoid conflicts with containers
            // created by other workflows that use the same name but different
            // networking/port exposure settings.
            await RemoveContainerIfExistsAsync();
            builder = builder
                .WithName(SharedContainerName)
                .WithPortBinding(SharedPort, ContainerPort)
                .WithCleanUp(false);
        }
        else
        {
            // Unique name and a random host port (the builder's default binding). Docker removes
            // the container as soon as it stops (auto-remove), so no leftovers accumulate even
            // where the resource reaper is unavailable (inside the ANDO build container).
            builder = builder
                .WithName($"{SharedContainerName}-{Guid.NewGuid():N}")
                .WithAutoRemove(true)
                .WithCleanUp(true);
        }

        // Inside a container (ANDO build) the database must share the runner's network so it
        // can be reached directly; see TestcontainersEnvironment.
        var runnerNetwork = await TestcontainersEnvironment.GetRunnerNetworkNameAsync();
        if (runnerNetwork != null)
        {
            builder = builder.WithNetwork(runnerNetwork);
        }

        _container = builder.Build();
        await _container.StartAsync();
        _connectionString = WithEndpoint(_container.GetConnectionString(),
            TestcontainersEnvironment.DirectEndpoint(_container, ContainerPort));
    }

    /// <summary>
    /// Shared mode: leaves the container running for data inspection.
    /// Isolated mode: stops and removes the container.
    /// </summary>
    public async Task DisposeAsync()
    {
        // Shared mode: to get a fresh database, remove the container manually:
        //   docker rm -f ando-server-tests-postgres
        if (_mode == DatabaseContainerMode.Isolated && _container != null)
        {
            await _container.StopAsync();
            await _container.DisposeAsync();
        }
    }

    private static async Task RemoveContainerIfExistsAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("rm");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(SharedContainerName);

        using var process = Process.Start(psi);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }
    }

    /// <summary>
    /// Creates a connection string for a specific database name.
    /// Use this to give each test class its own isolated database.
    /// </summary>
    /// <param name="databaseName">Database name to target.</param>
    /// <returns>The container connection string pointed at <paramref name="databaseName"/>.</returns>
    // Points the Testcontainers connection string at the endpoint the test process can reach:
    // the mapped host port on a developer machine, the container's own IP inside a container.
    private static string WithEndpoint(string connectionString, (string Host, int Port) endpoint)
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString)
        {
            Host = endpoint.Host,
            Port = endpoint.Port
        };
        return builder.ConnectionString;
    }

    public string GetConnectionString(string databaseName)
    {
        var builder = new NpgsqlConnectionStringBuilder(ConnectionString)
        {
            Database = databaseName
        };
        return builder.ConnectionString;
    }
}

/// <summary>
/// xUnit collection definition for tests that share the PostgreSQL container.
/// </summary>
[CollectionDefinition(Name)]
public class PostgreSqlCollection : ICollectionFixture<PostgreSqlFixture>
{
    /// <summary>
    /// Collection name.
    /// </summary>
    public const string Name = "PostgreSql";
}
