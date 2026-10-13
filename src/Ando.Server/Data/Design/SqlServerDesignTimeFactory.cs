// =============================================================================
// SqlServerDesignTimeFactory.cs
//
// Summary: Design-time factory used by `dotnet ef` for SqlServerAndoDbContext.
//
// The application host registers only the context for the configured provider,
// so `dotnet ef` cannot resolve both provider contexts from Program.cs. EF
// prefers an IDesignTimeDbContextFactory over the host, which makes migration
// commands work for either context regardless of runtime configuration.
//
// Design Decisions:
// - Connection string comes from ANDO_DB_CONNECTION, falling back to a dummy
//   value; scaffolding and snapshot diffing never open a connection
// - Commands that do need a database (update, script --idempotent against a
//   live server) must set ANDO_DB_CONNECTION explicitly
// =============================================================================

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Ando.Server.Data.Design;

/// <summary>
/// Creates <see cref="SqlServerAndoDbContext"/> instances for EF Core design-time tooling.
/// </summary>
public class SqlServerDesignTimeFactory : IDesignTimeDbContextFactory<SqlServerAndoDbContext>
{
    /// <summary>
    /// Environment variable that overrides the design-time connection string.
    /// </summary>
    public const string ConnectionEnvironmentVariable = "ANDO_DB_CONNECTION";

    private const string DefaultConnectionString =
        "Server=localhost;Database=AndoServer;User Id=sa;Password=x;TrustServerCertificate=true";

    /// <inheritdoc />
    public SqlServerAndoDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = DefaultConnectionString;
        }

        var options = new DbContextOptionsBuilder<SqlServerAndoDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new SqlServerAndoDbContext(options);
    }
}
