// =============================================================================
// DatabaseServiceExtensions.cs
//
// Summary: Single switch point that registers the EF Core context and the
// Hangfire job storage for the configured database provider.
//
// Program.cs calls AddAndoDatabase() once. Everything that depends on the
// database provider (DbContext type, EF provider, Hangfire storage, Hangfire
// server) is decided here from Database:Provider.
//
// Design Decisions:
// - AndoDbContext resolves to a provider-specific subclass so each provider
//   keeps its own migration set while consumers keep injecting AndoDbContext
// - Testing: no EF registration (WebApplicationFactory configures it) and
//   Hangfire uses in-memory storage without a server, so tests never need a
//   SQL Server for Hangfire
// - E2E: EF is registered normally, Hangfire is replaced by no-op services
// - Hangfire storage options come from HangfireStorageOptionsFactory, shared
//   with the real-storage tests; PostgreSQL storage enables sliding
//   invisibility timeout so a long build is never picked up by a second worker
// - Provider/connection validation (ReadValidatedConnection) and the DbContext
//   registration (AddAndoDbContext) are public so the export/import CLI mode
//   (DataPortability) registers EF exactly like the web host, without Hangfire
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Infrastructure.Hangfire;
using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Infrastructure.Database;

/// <summary>
/// Registers database-related services for the configured provider.
/// </summary>
public static class DatabaseServiceExtensions
{
    /// <summary>
    /// Registers <see cref="AndoDbContext"/> and Hangfire storage for the provider
    /// selected by <c>Database:Provider</c>.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown outside Testing when the provider is unknown or the connection string is missing or invalid.
    /// </exception>
    public static WebApplicationBuilder AddAndoDatabase(this WebApplicationBuilder builder)
    {
        var services = builder.Services;
        var configuration = builder.Configuration;
        var environment = builder.Environment;

        // Parse the provider by hand (see DatabaseConfiguration) and publish the
        // result as DatabaseSettings, so IOptions<DatabaseSettings> never throws
        // on a bad value after startup validation has passed.
        var providerValid = DatabaseConfiguration.TryReadProvider(configuration, out var provider, out var providerError);
        services.Configure<DatabaseSettings>(settings => settings.Provider = provider);

        // Testing: WebApplicationFactory registers the DbContext. Hangfire gets
        // in-memory storage so JobStorage resolves, but no server is started;
        // tests replace IBackgroundJobClient/IRecurringJobManager with mocks.
        if (environment.IsEnvironment("Testing"))
        {
            services.AddHangfire(config => ConfigureHangfireCommon(config).UseInMemoryStorage());
            return builder;
        }

        if (!providerValid)
        {
            throw new InvalidOperationException(providerError);
        }

        var (_, connectionString) = ReadValidatedConnection(configuration);
        services.AddAndoDbContext(provider, connectionString);

        // E2E: no background job processing; BuildService still needs the abstractions.
        if (environment.IsEnvironment("E2E"))
        {
            services.AddSingleton<IBackgroundJobClient, NoOpBackgroundJobClient>();
            services.AddSingleton<IRecurringJobManager, NoOpRecurringJobManager>();
            return builder;
        }

        services.AddHangfire(config =>
        {
            var common = ConfigureHangfireCommon(config);
            if (provider == DatabaseProvider.PostgreSql)
            {
                common.UsePostgreSqlStorage(
                    options => options.UseNpgsqlConnection(connectionString),
                    HangfireStorageOptionsFactory.CreatePostgreSqlOptions());
            }
            else
            {
                common.UseSqlServerStorage(connectionString, HangfireStorageOptionsFactory.CreateSqlServerOptions());
            }
        });

        var buildSettings = configuration
            .GetSection(BuildSettings.SectionName)
            .Get<BuildSettings>() ?? new BuildSettings();

        services.AddHangfireServer(options =>
        {
            options.WorkerCount = buildSettings.WorkerCount;
            options.Queues = ["builds", "default"];
        });

        return builder;
    }

    /// <summary>
    /// Reads <c>Database:Provider</c> and <c>ConnectionStrings:DefaultConnection</c> and validates
    /// that the connection string parses with the provider's builder.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The selected provider and its connection string.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the provider is unknown or the connection string is missing or invalid.
    /// </exception>
    public static (DatabaseProvider Provider, string ConnectionString) ReadValidatedConnection(IConfiguration configuration)
    {
        if (!DatabaseConfiguration.TryReadProvider(configuration, out var provider, out var providerError))
        {
            throw new InvalidOperationException(providerError);
        }

        var connectionString = configuration.GetConnectionString(DatabaseConfiguration.ConnectionStringName);
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
        }

        if (!DatabaseConfiguration.TryValidateConnectionString(provider, connectionString, out var connectionError))
        {
            throw new InvalidOperationException(connectionError);
        }

        return (provider, connectionString);
    }

    /// <summary>
    /// Registers <see cref="AndoDbContext"/> as the provider-specific subclass
    /// (<see cref="SqlServerAndoDbContext"/> or <see cref="PostgreSqlAndoDbContext"/>).
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="provider">The database provider.</param>
    /// <param name="connectionString">A connection string validated for <paramref name="provider"/>.</param>
    /// <returns>The same service collection for chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown for an unsupported provider.</exception>
    public static IServiceCollection AddAndoDbContext(this IServiceCollection services, DatabaseProvider provider, string connectionString)
    {
        switch (provider)
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
                throw new InvalidOperationException($"Unsupported database provider '{provider}'.");
        }

        return services;
    }

    /// <summary>
    /// Logs the selected provider and connection target (no credentials) once the app is built.
    /// Skipped in Testing, where the test factory owns the database registration.
    /// </summary>
    /// <param name="app">The built web application.</param>
    public static void LogAndoDatabaseTarget(this WebApplication app)
    {
        if (app.Environment.IsEnvironment("Testing"))
        {
            return;
        }

        // Values were validated in AddAndoDatabase, so these calls cannot fail here.
        DatabaseConfiguration.TryReadProvider(app.Configuration, out var provider, out _);
        var connectionString = app.Configuration.GetConnectionString(DatabaseConfiguration.ConnectionStringName) ?? "";

        app.Logger.LogInformation(
            "Database provider {Provider}, target {Target}",
            provider,
            DatabaseConfiguration.DescribeTarget(provider, connectionString));
    }

    private static IGlobalConfiguration ConfigureHangfireCommon(IGlobalConfiguration config)
    {
        return config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings();
    }
}
