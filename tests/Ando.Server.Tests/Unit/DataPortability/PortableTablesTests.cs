// =============================================================================
// PortableTablesTests.cs
//
// Summary: Unit tests for the exported table list and the provider-neutral
// model fingerprint, built from the real SQL Server and PostgreSQL models
// (no database connection needed).
//
// Design Decisions:
// - The fingerprint must be equal for both providers, otherwise a SQL Server
//   export could never be imported into PostgreSQL; the test fails if a
//   provider-specific configuration leaks into it
// - Resolve() also asserts every mapped table is exported, so a new entity
//   fails here until it is added to PortableTables
// - The store-default audit pins the columns whose sentinel values the import
//   restores after inserting; a new HasDefaultValue shows up here for review.
//   AllowUserRegistration's sentinel is true (its store default), so EF always
//   inserts false and only TimeoutMinutes = 0 ever needs restoring
// =============================================================================

using Ando.Server.Data;
using Ando.Server.DataPortability;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Tests.Unit.DataPortability;

/// <summary>
/// Tests for <see cref="PortableTables"/> and <see cref="ModelFingerprint"/>.
/// </summary>
public class PortableTablesTests
{
    private static AndoDbContext CreateContext(string provider) => provider switch
    {
        "SqlServer" => new SqlServerAndoDbContext(
            new DbContextOptionsBuilder<SqlServerAndoDbContext>()
                .UseSqlServer("Server=localhost;Database=x;User Id=sa;Password=x;TrustServerCertificate=true")
                .Options),
        "PostgreSql" => new PostgreSqlAndoDbContext(
            new DbContextOptionsBuilder<PostgreSqlAndoDbContext>()
                .UseNpgsql("Host=localhost;Database=x;Username=x;Password=x")
                .Options),
        _ => throw new ArgumentOutOfRangeException(nameof(provider))
    };

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void Resolve_ReturnsTablesInForeignKeyOrder(string provider)
    {
        using var db = CreateContext(provider);

        var tables = PortableTables.Resolve(db.Model);

        tables.Select(t => t.Name).ShouldBe(PortableTables.TableNames);
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void StoreDefaultProperties_AreTheAuditedColumns(string provider)
    {
        using var db = CreateContext(provider);

        var columns = PortableTables.Resolve(db.Model)
            .SelectMany(t => DataImportService.StoreDefaultProperties(t).Select(p => $"{t.Name}.{p.Name}={p.Sentinel ?? "null"}"))
            .Order(StringComparer.Ordinal)
            .ToList();

        columns.ShouldBe(
        [
            "Projects.BranchFilter=null",
            "Projects.DefaultBranch=null",
            "Projects.TimeoutMinutes=0",
            "SystemSettings.AllowUserRegistration=True"
        ]);
    }

    [Theory]
    [InlineData("SqlServer")]
    [InlineData("PostgreSql")]
    public void Resolve_IdentityTables_AreSingleGeneratedIntegerKeys(string provider)
    {
        using var db = CreateContext(provider);

        var identityTables = PortableTables.Resolve(db.Model)
            .Where(t => t.IdentityKey != null)
            .Select(t => t.Name);

        identityTables.ShouldBe(
        [
            "AspNetRoles", "AspNetUsers", "AspNetUserClaims", "AspNetRoleClaims",
            "Projects", "ProjectSecrets", "Builds", "BuildLogEntries", "BuildArtifacts", "ApiTokens"
        ]);
    }

    [Fact]
    public void Resolve_DoesNotExportComputedProperties()
    {
        using var db = CreateContext("SqlServer");

        var builds = PortableTables.Resolve(db.Model).Single(t => t.Name == "Builds");

        builds.Properties.Select(p => p.Name).ShouldNotContain("ShortCommitSha");
        builds.Properties.Select(p => p.Name).ShouldContain("HangfireJobId");
    }

    [Fact]
    public void Fingerprint_IsEqualForSqlServerAndPostgreSql()
    {
        using var sqlServer = CreateContext("SqlServer");
        using var postgreSql = CreateContext("PostgreSql");

        var sqlDescription = ModelFingerprint.Describe(PortableTables.Resolve(sqlServer.Model));
        var pgDescription = ModelFingerprint.Describe(PortableTables.Resolve(postgreSql.Model));

        pgDescription.ShouldBe(sqlDescription);
        ModelFingerprint.Compute(PortableTables.Resolve(sqlServer.Model))
            .ShouldBe(ModelFingerprint.Compute(PortableTables.Resolve(postgreSql.Model)));
    }

    [Fact]
    public void Fingerprint_DescribesColumnShape()
    {
        using var db = CreateContext("SqlServer");

        var description = ModelFingerprint.Describe(PortableTables.Resolve(db.Model));

        description.ShouldContain("table ProjectSecrets\n");
        description.ShouldContain("  NormalizedName System.String notnull max=100\n");
        description.ShouldContain("  Id System.Int64 notnull max=- key\n");
    }

    [Theory]
    [InlineData("20261013075421_AddProjectSecretNormalizedName", "AddProjectSecretNormalizedName")]
    [InlineData("20261013071415_InitialCreate", "InitialCreate")]
    [InlineData("NoTimestamp", "NoTimestamp")]
    [InlineData("", "")]
    public void MigrationName_StripsTimestamp(string id, string expected)
    {
        ExportManifest.MigrationName(id).ShouldBe(expected);
    }
}
