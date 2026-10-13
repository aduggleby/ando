// =============================================================================
// HangfireStorageOptionsFactory.cs
//
// Summary: Single source of the Hangfire job storage options for each database
// provider, used by production registration and by the real-storage tests.
//
// The options that matter for correctness (lease renewal while a long build is
// running, schema name) live only here. Tests may shorten timings but cannot
// construct storage options of their own, so disabling lease renewal in
// production also changes what the Hangfire storage tests exercise.
//
// Design Decisions:
// - Only timings are overridable (invisibility/sliding timeout, poll interval);
//   everything else is fixed production configuration
// - PostgreSQL: UseSlidingInvisibilityTimeout = true. Without it a build running
//   longer than InvisibilityTimeout becomes visible again and is picked up by a
//   second worker while the first is still running
// - SQL Server: SlidingInvisibilityTimeout set (lease renewal) and
//   QueuePollInterval = 0 (long polling), as recommended by Hangfire
// - internal: visible to Ando.Server.Tests via InternalsVisibleTo
// =============================================================================

using Hangfire.PostgreSql;
using Hangfire.SqlServer;

namespace Ando.Server.Infrastructure.Database;

/// <summary>
/// Builds the production Hangfire storage options, with optional timing overrides for tests.
/// </summary>
internal static class HangfireStorageOptionsFactory
{
    /// <summary>
    /// PostgreSQL schema that holds the Hangfire tables.
    /// </summary>
    public const string PostgreSqlSchemaName = "hangfire";

    /// <summary>
    /// Default lease length; renewed while the job runs (sliding).
    /// </summary>
    public static readonly TimeSpan DefaultInvisibilityTimeout = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Creates the SQL Server storage options used in production.
    /// </summary>
    /// <param name="invisibilityTimeout">Overrides the sliding invisibility timeout (tests only).</param>
    /// <param name="pollInterval">Overrides the queue poll interval (tests only); production long-polls.</param>
    /// <returns>The storage options.</returns>
    public static SqlServerStorageOptions CreateSqlServerOptions(
        TimeSpan? invisibilityTimeout = null,
        TimeSpan? pollInterval = null)
    {
        return new SqlServerStorageOptions
        {
            CommandBatchMaxTimeout = TimeSpan.FromMinutes(5),
            SlidingInvisibilityTimeout = invisibilityTimeout ?? DefaultInvisibilityTimeout,
            QueuePollInterval = pollInterval ?? TimeSpan.Zero,
            UseRecommendedIsolationLevel = true,
            DisableGlobalLocks = true
        };
    }

    /// <summary>
    /// Creates the PostgreSQL storage options used in production.
    /// </summary>
    /// <param name="invisibilityTimeout">Overrides the invisibility timeout (tests only).</param>
    /// <param name="pollInterval">Overrides the queue poll interval (tests only).</param>
    /// <returns>The storage options.</returns>
    public static PostgreSqlStorageOptions CreatePostgreSqlOptions(
        TimeSpan? invisibilityTimeout = null,
        TimeSpan? pollInterval = null)
    {
        return new PostgreSqlStorageOptions
        {
            SchemaName = PostgreSqlSchemaName,
            QueuePollInterval = pollInterval ?? TimeSpan.FromSeconds(1),
            InvisibilityTimeout = invisibilityTimeout ?? DefaultInvisibilityTimeout,
            UseSlidingInvisibilityTimeout = true
        };
    }
}
