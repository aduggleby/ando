// =============================================================================
// PostgreSqlAndoDbContext.cs
//
// Summary: PostgreSQL-specific subclass of AndoDbContext.
//
// EF Core ties migrations to a context type via the [DbContext] attribute, so
// each provider needs its own context type to own its own migration set
// (Migrations/PostgreSql/). The model itself is defined once in AndoDbContext.
//
// Design Decisions:
// - Empty apart from the constructor; all model configuration stays in the base
// - Registered as AddDbContext<AndoDbContext, PostgreSqlAndoDbContext>() so consumers
//   keep injecting AndoDbContext
// =============================================================================

using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Data;

/// <summary>
/// PostgreSQL variant of <see cref="AndoDbContext"/> that owns the PostgreSQL migrations.
/// </summary>
public class PostgreSqlAndoDbContext : AndoDbContext
{
    /// <summary>
    /// Creates the PostgreSQL context.
    /// </summary>
    /// <param name="options">Options configured for <see cref="PostgreSqlAndoDbContext"/>.</param>
    public PostgreSqlAndoDbContext(DbContextOptions<PostgreSqlAndoDbContext> options) : base(options)
    {
    }
}
