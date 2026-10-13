// =============================================================================
// SqlServerAndoDbContext.cs
//
// Summary: SQL Server-specific subclass of AndoDbContext.
//
// EF Core ties migrations to a context type via the [DbContext] attribute, so
// each provider needs its own context type to own its own migration set
// (Migrations/SqlServer/). The model itself is defined once in AndoDbContext.
//
// Design Decisions:
// - Empty apart from the constructor; all model configuration stays in the base
// - Registered as AddDbContext<AndoDbContext, SqlServerAndoDbContext>() so consumers
//   keep injecting AndoDbContext
// =============================================================================

using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Data;

/// <summary>
/// SQL Server variant of <see cref="AndoDbContext"/> that owns the SQL Server migrations.
/// </summary>
public class SqlServerAndoDbContext : AndoDbContext
{
    /// <summary>
    /// Creates the SQL Server context.
    /// </summary>
    /// <param name="options">Options configured for <see cref="SqlServerAndoDbContext"/>.</param>
    public SqlServerAndoDbContext(DbContextOptions<SqlServerAndoDbContext> options) : base(options)
    {
    }
}
