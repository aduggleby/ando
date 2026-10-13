// =============================================================================
// DatabaseSettings.cs
//
// Summary: Database provider configuration settings.
//
// Selects which relational database Ando Server uses for both the EF Core
// application data and the Hangfire job storage. The connection string itself
// stays in ConnectionStrings:DefaultConnection.
//
// Design Decisions:
// - Defaults to SqlServer so existing installs keep working without changes
// - Bound as an enum so an unknown provider fails fast at startup
// - Set via environment variable Database__Provider in containers
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Supported relational database providers.
/// </summary>
public enum DatabaseProvider
{
    /// <summary>
    /// Microsoft SQL Server (default).
    /// </summary>
    SqlServer,

    /// <summary>
    /// PostgreSQL.
    /// </summary>
    PostgreSql
}

/// <summary>
/// Database configuration settings.
/// </summary>
public class DatabaseSettings
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "Database";

    /// <summary>
    /// The database provider used for application data and Hangfire job storage.
    /// </summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.SqlServer;
}
