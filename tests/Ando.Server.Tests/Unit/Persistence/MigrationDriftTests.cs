// =============================================================================
// MigrationDriftTests.cs
//
// Summary: Fails when the EF model and a provider's migration set drift apart,
// i.e. a model change was made without adding a migration for that provider.
//
// Replaces a CI step running `dotnet ef migrations has-pending-model-changes`
// per context. EF Core 9's Database.HasPendingModelChanges() compares the
// current model against the last migration's model snapshot without opening a
// connection, so this runs as a plain unit test.
//
// Design Decisions:
// - Dummy connection strings: no database is contacted
// - One test per context so a failure names the provider whose migrations
//   are missing (fix with scripts/add-migration.sh <Name>)
// - The last SQL Server migration is pinned so an accidental removal or a
//   migration added for one provider only is noticed
// =============================================================================

using Ando.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Tests.Unit.Persistence;

/// <summary>
/// Checks both providers' migration sets against the current model.
/// </summary>
public class MigrationDriftTests
{
    private const string LastSqlServerMigration = "20261013075421_AddProjectSecretNormalizedName";

    [Fact]
    public void SqlServer_HasNoPendingModelChanges()
    {
        using var context = CreateSqlServerContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse(
            "The model changed without a SQL Server migration. Run scripts/add-migration.sh <Name>.");
    }

    [Fact]
    public void PostgreSql_HasNoPendingModelChanges()
    {
        using var context = CreatePostgreSqlContext();

        context.Database.HasPendingModelChanges().ShouldBeFalse(
            "The model changed without a PostgreSQL migration. Run scripts/add-migration.sh <Name>.");
    }

    [Fact]
    public void SqlServer_MigrationsEndWithNormalizedNameMigration()
    {
        using var context = CreateSqlServerContext();

        var migrations = context.Database.GetMigrations().ToList();

        migrations.ShouldNotBeEmpty();
        migrations[^1].ShouldBe(LastSqlServerMigration);
    }

    [Fact]
    public void PostgreSql_HasMigrations()
    {
        using var context = CreatePostgreSqlContext();

        context.Database.GetMigrations().ShouldNotBeEmpty();
    }

    private static SqlServerAndoDbContext CreateSqlServerContext()
    {
        var options = new DbContextOptionsBuilder<SqlServerAndoDbContext>()
            .UseSqlServer("Server=localhost;Database=DriftCheck;User Id=sa;Password=unused;TrustServerCertificate=True")
            .Options;
        return new SqlServerAndoDbContext(options);
    }

    private static PostgreSqlAndoDbContext CreatePostgreSqlContext()
    {
        var options = new DbContextOptionsBuilder<PostgreSqlAndoDbContext>()
            .UseNpgsql("Host=localhost;Database=DriftCheck;Username=unused;Password=unused")
            .Options;
        return new PostgreSqlAndoDbContext(options);
    }
}
