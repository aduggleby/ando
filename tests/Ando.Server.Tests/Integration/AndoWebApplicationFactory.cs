// =============================================================================
// AndoWebApplicationFactory.cs
//
// Summary: Custom WebApplicationFactory for integration testing.
//
// Configures the test server with a real database (SQL Server or PostgreSQL via
// Testcontainers) and mock external services. Allows integration tests to
// exercise the full HTTP pipeline while controlling external dependencies.
//
// Design Decisions:
// - Uses a real database for accurate behavior; the provider is chosen by the
//   caller and mirrors the production registration (AndoDbContext resolves to
//   the provider-specific subclass)
// - Mocks Hangfire client/recurring manager; JobStorage stays the in-memory
//   storage that Program.cs registers for the Testing environment
// - Provides access to database for test setup and verification
// - Falls back to in-memory database if no connection string provided
// - Settings adds host configuration (UseSetting) visible to Program.cs while
//   the builder is configured, e.g. Logging:File:* for the file logging test
// =============================================================================

using Ando.Server.BuildExecution;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Hangfire;
using Hangfire.States;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Custom WebApplicationFactory for integration tests.
/// </summary>
public class AndoWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly DatabaseProvider _provider;
    private readonly string? _connectionString;
    private readonly string _databaseName = $"AndoTest_{Guid.NewGuid():N}";

    /// <summary>
    /// The mock Hangfire job client for verifying job enqueuing.
    /// </summary>
    public Mock<IBackgroundJobClient> MockJobClient { get; } = new();

    /// <summary>
    /// List of job IDs that have been "enqueued".
    /// </summary>
    public List<string> EnqueuedJobIds { get; } = [];

    /// <summary>
    /// Webhook secret used for signature validation.
    /// </summary>
    public string WebhookSecret { get; set; } = "test-webhook-secret-for-integration";

    /// <summary>
    /// Extra configuration values applied with UseSetting before the host is built.
    /// </summary>
    public Dictionary<string, string?> Settings { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Creates a new factory using in-memory database (for unit tests).
    /// </summary>
    public AndoWebApplicationFactory() : this(null)
    {
    }

    /// <summary>
    /// Creates a new factory using the specified SQL Server connection string.
    /// </summary>
    /// <param name="connectionString">SQL Server connection string, or null for in-memory database.</param>
    public AndoWebApplicationFactory(string? connectionString)
        : this(DatabaseProvider.SqlServer, connectionString)
    {
    }

    /// <summary>
    /// Creates a new factory for the given database provider.
    /// </summary>
    /// <param name="provider">Database provider the connection string belongs to.</param>
    /// <param name="connectionString">Provider connection string, or null for in-memory database.</param>
    public AndoWebApplicationFactory(DatabaseProvider provider, string? connectionString)
    {
        _provider = provider;
        _connectionString = connectionString;

        // Setup mock job client to return incrementing job IDs
        var jobCounter = 0;
        MockJobClient
            .Setup(x => x.Create(It.IsAny<Hangfire.Common.Job>(), It.IsAny<IState>()))
            .Returns(() =>
            {
                var jobId = $"test-job-{++jobCounter}";
                EnqueuedJobIds.Add(jobId);
                return jobId;
            });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        foreach (var (key, value) in Settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureServices(services =>
        {
            // Remove all DbContext-related registrations to avoid provider conflicts
            var dbContextTypes = new HashSet<Type>
            {
                typeof(AndoDbContext),
                typeof(SqlServerAndoDbContext),
                typeof(PostgreSqlAndoDbContext),
                typeof(DbContextOptions)
            };
            var dbContextDescriptors = services
                .Where(d => dbContextTypes.Contains(d.ServiceType)
                    || (d.ServiceType.IsGenericType
                        && d.ServiceType.GetGenericTypeDefinition() == typeof(DbContextOptions<>))
                    || d.ServiceType.FullName?.Contains("EntityFrameworkCore") == true)
                .ToList();

            foreach (var descriptor in dbContextDescriptors)
            {
                services.Remove(descriptor);
            }

            // Use the requested provider if a connection string is provided, otherwise in-memory
            if (!string.IsNullOrEmpty(_connectionString))
            {
                var connectionString = BuildDatabaseConnectionString();
                switch (_provider)
                {
                    case DatabaseProvider.SqlServer:
                        services.AddDbContext<AndoDbContext, SqlServerAndoDbContext>(options =>
                            options.UseSqlServer(connectionString));
                        break;
                    case DatabaseProvider.PostgreSql:
                        services.AddDbContext<AndoDbContext, PostgreSqlAndoDbContext>(options =>
                            options.UseNpgsql(connectionString));
                        break;
                    default:
                        throw new InvalidOperationException($"Unsupported database provider '{_provider}'.");
                }

                // Report the provider under test (e.g. via the health endpoint).
                services.Configure<DatabaseSettings>(options => options.Provider = _provider);
            }
            else
            {
                // Fall back to in-memory database for simple unit tests
                services.AddDbContext<AndoDbContext>(options =>
                {
                    options.UseInMemoryDatabase(_databaseName);
                });
            }

            // Remove real Hangfire services
            services.RemoveAll<IBackgroundJobClient>();
            services.RemoveAll<IRecurringJobManager>();

            // Add mock Hangfire client
            services.AddSingleton(MockJobClient.Object);
            services.AddSingleton(new Mock<IRecurringJobManager>().Object);

            // Configure settings for testing
            services.Configure<GitHubSettings>(options =>
            {
                options.WebhookSecret = WebhookSecret;
                options.ClientId = "test-client-id";
                options.ClientSecret = "test-client-secret";
                options.AppId = "12345";
            });

            services.Configure<EncryptionSettings>(options =>
            {
                // Valid 32-byte key for AES-256
                options.Key = "MDEyMzQ1Njc4OWFiY2RlZjAxMjM0NTY3ODlhYmNkZWY=";
            });

            services.Configure<StorageSettings>(options =>
            {
                options.ArtifactsPath = "/tmp/ando-test-artifacts";
                options.ArtifactRetentionDays = 30;
            });

            services.Configure<BuildSettings>(options =>
            {
                options.DefaultTimeoutMinutes = 15;
                options.MaxTimeoutMinutes = 60;
                options.WorkerCount = 1;
            });

            services.Configure<TestSettings>(options =>
            {
                options.ApiKey = "test-api-key-for-integration-tests";
            });

            // Register CancellationTokenRegistry as singleton (required by BuildService)
            services.RemoveAll<CancellationTokenRegistry>();
            services.AddSingleton<CancellationTokenRegistry>();
        });
    }

    /// <summary>
    /// Creates a new scope and returns the database context for test setup.
    /// </summary>
    public AndoDbContext CreateDbContext()
    {
        var scope = Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<AndoDbContext>();
    }

    /// <summary>
    /// Gets a fresh database context using an existing scope.
    /// </summary>
    public AndoDbContext GetDbContext(IServiceScope scope)
    {
        return scope.ServiceProvider.GetRequiredService<AndoDbContext>();
    }

    /// <summary>
    /// Initializes the database by recreating it from the current model (EnsureCreated).
    /// Call this before running tests when using a container-backed provider.
    /// </summary>
    public async Task InitializeDatabaseAsync()
    {
        using var scope = Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AndoDbContext>();

        // For container-backed providers, ensure the database is created fresh
        if (!string.IsNullOrEmpty(_connectionString))
        {
            await context.Database.EnsureDeletedAsync();
            await context.Database.EnsureCreatedAsync();
        }
    }

    /// <summary>
    /// Returns the configured connection string pointed at this factory's unique database.
    /// </summary>
    private string BuildDatabaseConnectionString()
    {
        return _provider switch
        {
            DatabaseProvider.SqlServer => new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(_connectionString)
            {
                InitialCatalog = _databaseName
            }.ConnectionString,
            DatabaseProvider.PostgreSql => new Npgsql.NpgsqlConnectionStringBuilder(_connectionString)
            {
                Database = _databaseName
            }.ConnectionString,
            _ => throw new InvalidOperationException($"Unsupported database provider '{_provider}'.")
        };
    }
}
