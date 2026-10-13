// =============================================================================
// DatabaseConfiguration.cs
//
// Summary: Shared helpers for reading and validating the database provider and
// connection string.
//
// Used by both DatabaseServiceExtensions (startup registration, fails fast)
// and ConfigurationValidator (collects readable errors for the error page) so
// the two code paths cannot disagree about what is valid.
//
// Design Decisions:
// - The provider is parsed by hand instead of relying on the configuration
//   binder: the binder throws an unhelpful exception for unknown names and
//   silently accepts undefined numeric values such as "5"
// - Connection strings are parsed with the provider's own builder, so a SQL
//   Server string paired with Database__Provider=PostgreSql is caught early
// - Startup log output describes host and database only, never credentials
// =============================================================================

using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Ando.Server.Configuration;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Ando.Server.Infrastructure.Database;

/// <summary>
/// Reads and validates database provider settings.
/// </summary>
public static class DatabaseConfiguration
{
    /// <summary>
    /// Name of the connection string used for application data and Hangfire storage.
    /// </summary>
    public const string ConnectionStringName = "DefaultConnection";

    /// <summary>
    /// Configuration key of the provider setting.
    /// </summary>
    public const string ProviderKey = DatabaseSettings.SectionName + ":" + nameof(DatabaseSettings.Provider);

    /// <summary>
    /// Environment variable equivalent of <see cref="ProviderKey"/>.
    /// </summary>
    public const string ProviderEnvironmentVariable = "Database__Provider";

    /// <summary>
    /// Reads <c>Database:Provider</c>, defaulting to <see cref="DatabaseProvider.SqlServer"/> when unset.
    /// </summary>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="provider">The parsed provider when successful.</param>
    /// <param name="error">A readable error naming the valid values when parsing fails.</param>
    /// <returns><c>true</c> when the value is unset or a known provider name.</returns>
    public static bool TryReadProvider(IConfiguration configuration, out DatabaseProvider provider, [NotNullWhen(false)] out string? error)
    {
        provider = DatabaseProvider.SqlServer;
        error = null;

        var raw = configuration[ProviderKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }

        var value = raw.Trim();
        var isNumeric = int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
        // Reject numeric values and flag combinations ("SqlServer,PostgreSql"),
        // which Enum.TryParse would otherwise accept.
        if (!isNumeric
            && !value.Contains(',')
            && Enum.TryParse(value, ignoreCase: true, out DatabaseProvider parsed)
            && Enum.IsDefined(parsed))
        {
            provider = parsed;
            return true;
        }

        error =
            $"{ProviderKey} has unsupported value '{value}'. Valid values: " +
            $"{string.Join(", ", Enum.GetNames<DatabaseProvider>())}. " +
            $"Set via environment variable: {ProviderEnvironmentVariable}";
        return false;
    }

    /// <summary>
    /// Checks that the connection string parses with the selected provider's builder.
    /// </summary>
    /// <param name="provider">Selected database provider.</param>
    /// <param name="connectionString">Connection string to validate.</param>
    /// <param name="error">A readable error when the string does not parse.</param>
    /// <returns><c>true</c> when the connection string is valid for the provider.</returns>
    public static bool TryValidateConnectionString(DatabaseProvider provider, string connectionString, [NotNullWhen(false)] out string? error)
    {
        error = null;
        try
        {
            _ = CreateBuilder(provider, connectionString);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or KeyNotFoundException or InvalidOperationException)
        {
            error =
                $"ConnectionStrings:{ConnectionStringName} is not a valid {provider} connection string ({ex.Message}). " +
                $"Check that {ProviderEnvironmentVariable} matches the connection string format.";
            return false;
        }
    }

    /// <summary>
    /// Describes the connection target (host and database) without credentials, for logging.
    /// </summary>
    /// <param name="provider">Selected database provider.</param>
    /// <param name="connectionString">A connection string that already passed validation.</param>
    /// <returns>A credential-free description such as <c>host=db:5432 database=AndoServer</c>.</returns>
    public static string DescribeTarget(DatabaseProvider provider, string connectionString)
    {
        return CreateBuilder(provider, connectionString) switch
        {
            SqlConnectionStringBuilder sql => $"server={sql.DataSource} database={sql.InitialCatalog}",
            NpgsqlConnectionStringBuilder pg => $"host={pg.Host}:{pg.Port} database={pg.Database}",
            _ => "(unknown)"
        };
    }

    private static System.Data.Common.DbConnectionStringBuilder CreateBuilder(DatabaseProvider provider, string connectionString)
    {
        return provider switch
        {
            DatabaseProvider.SqlServer => new SqlConnectionStringBuilder(connectionString),
            DatabaseProvider.PostgreSql => new NpgsqlConnectionStringBuilder(connectionString),
            _ => throw new InvalidOperationException($"Unsupported database provider '{provider}'.")
        };
    }
}
