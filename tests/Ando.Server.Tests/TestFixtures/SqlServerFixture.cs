// =============================================================================
// SqlServerFixture.cs
//
// Summary: Manages a SQL Server container for Ando.Server integration tests.
//
// Uses Testcontainers to spin up a real SQL Server instance in Docker.
// The container is shared across all tests in the collection for performance,
// and each test class gets its own database for isolation.
//
// Design Decisions:
// - Uses SQL Server 2022 for compatibility with production
// - Uses a dedicated container name to avoid conflicts with E2E docker-compose
// - Container is recreated for each test run to guarantee expected port mapping
// - Container is left running after tests for data inspection
// - Databases are NOT dropped - restart container for fresh state
// - Implements IAsyncLifetime for proper async setup/teardown
// =============================================================================

using System.Diagnostics;
using Ando.Server.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// xUnit fixture that manages a SQL Server container for integration tests.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private const string ContainerName = "ando-server-tests-sqlserver";
    private const string Password = "Test@Password123!";
    private const int Port = 11433; // Use fixed port for reuse

    private MsSqlContainer? _container;
    private string? _connectionString;

    /// <summary>
    /// Gets the connection string for the SQL Server container.
    /// </summary>
    public string ConnectionString => _connectionString
        ?? throw new InvalidOperationException("Container not initialized");

    public async Task InitializeAsync()
    {
        // Always recreate the container to avoid conflicts with containers
        // created by other workflows that use the same name but different
        // networking/port exposure settings.
        await RemoveContainerIfExistsAsync();

        // Create new container
        _container = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .WithName(ContainerName)
            .WithPassword(Password)
            .WithPortBinding(Port, 1433)
            .WithCleanUp(false)
            .Build();

        await _container.StartAsync();
        _connectionString = _container.GetConnectionString();
    }

    public Task DisposeAsync()
    {
        // Don't stop the container - leave it running for data inspection
        // To get a fresh database, restart the container manually:
        //   docker stop ando-server-tests-sqlserver && docker rm ando-server-tests-sqlserver
        return Task.CompletedTask;
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
        psi.ArgumentList.Add(ContainerName);

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
    public string GetConnectionString(string databaseName)
    {
        // Replace the default database in the connection string
        var builder = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(ConnectionString)
        {
            InitialCatalog = databaseName
        };
        return builder.ConnectionString;
    }

    /// <summary>
    /// Creates a database with the given name and applies migrations.
    /// If the database already exists, it will be reused.
    /// </summary>
    public async Task<AndoDbContext> CreateDatabaseAsync(string databaseName)
    {
        var connectionString = GetConnectionString(databaseName);

        var options = new DbContextOptionsBuilder<AndoDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        var context = new AndoDbContext(options);

        // Create database and apply migrations (no-op if already exists)
        await context.Database.EnsureCreatedAsync();

        return context;
    }
}

/// <summary>
/// xUnit collection definition for tests that share the SQL Server container.
/// </summary>
[CollectionDefinition(Name)]
public class SqlServerCollection : ICollectionFixture<SqlServerFixture>
{
    public const string Name = "SqlServer";
}
