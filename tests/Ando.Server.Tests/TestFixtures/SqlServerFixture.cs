// =============================================================================
// SqlServerFixture.cs
//
// Summary: Manages a SQL Server container for integration/E2E tests.
//
// Uses Testcontainers to spin up a real SQL Server instance in Docker.
// The container is shared across all tests in the collection for performance,
// and each test class gets its own database for isolation.
//
// Design Decisions:
// - Uses SQL Server 2022 for compatibility with production
// - Container is named "ando-e2e-sqlserver" for easy identification
// - Container is left running after tests for data inspection
// - Databases are NOT dropped - restart container for fresh state
// - Reuses existing container if already running
// - Implements IAsyncLifetime for proper async setup/teardown
// =============================================================================

using System.Diagnostics;
using Ando.Server.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.MsSql;

namespace Ando.Server.Tests.TestFixtures;

/// <summary>
/// xUnit fixture that manages a SQL Server container for E2E tests.
/// </summary>
public class SqlServerFixture : IAsyncLifetime
{
    private const string ContainerName = "ando-e2e-sqlserver";
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
        // Check if container already exists and is running
        if (await IsContainerRunningAsync())
        {
            // Reuse existing container
            _connectionString = $"Server=localhost,{Port};Database=master;User Id=sa;Password={Password};TrustServerCertificate=true";
            return;
        }

        // Remove stopped container if it exists
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
        //   docker stop ando-e2e-sqlserver && docker rm ando-e2e-sqlserver
        return Task.CompletedTask;
    }

    private static async Task<bool> IsContainerRunningAsync()
    {
        var psi = new ProcessStartInfo
        {
            FileName = "docker",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("inspect");
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("{{.State.Running}}");
        psi.ArgumentList.Add(ContainerName);

        using var process = Process.Start(psi);
        if (process == null) return false;

        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 && output.Trim() == "true";
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
