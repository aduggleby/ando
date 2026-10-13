// =============================================================================
// ModelIdentifierTests.cs
//
// Summary: Guards the database identifiers produced by the shared model for
// both providers.
//
// PostgreSQL silently truncates identifiers longer than 63 characters, which
// can make two names collide or make migrations reference names that do not
// exist. These tests build the model for SqlServerAndoDbContext and
// PostgreSqlAndoDbContext and assert every table, column, index, key, and
// foreign key name fits, plus provider-specific configuration checks.
//
// Design Decisions:
// - Uses dummy connection strings; reading context.Model never opens a
//   connection, so no database is needed
// - Walks the relational model, which holds the final database names after
//   all conventions have run
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Ando.Server.Tests.Unit.Persistence;

public class ModelIdentifierTests
{
    private const int PostgreSqlMaxIdentifierLength = 63;

    public static TheoryData<string> Providers => new() { "SqlServer", "PostgreSql" };

    [Theory]
    [MemberData(nameof(Providers))]
    public void AllIdentifiers_Are63CharactersOrFewer(string provider)
    {
        using var context = CreateContext(provider);
        var names = CollectIdentifiers(context.Model.GetRelationalModel()).ToList();
        names.Add(("migrations history table", HistoryRepositoryTableName));

        names.Count.ShouldBeGreaterThan(50);
        var tooLong = names
            .Where(n => n.Name.Length > PostgreSqlMaxIdentifierLength)
            .Select(n => $"{n.Kind}: {n.Name} ({n.Name.Length})")
            .ToList();

        tooLong.ShouldBeEmpty();
    }

    [Fact]
    public void PostgreSqlModel_HasNoSqlServerBracketFilters()
    {
        using var context = CreateContext("PostgreSql");

        var bracketFilters = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetIndexes())
            .Select(i => i.GetFilter())
            .Where(f => f != null && f.Contains('['))
            .ToList();

        bracketFilters.ShouldBeEmpty();
    }

    [Fact]
    public void SqlServerModel_KeepsGitHubIdFilter()
    {
        using var context = CreateContext("SqlServer");

        var index = context.Model.FindEntityType(typeof(ApplicationUser))!
            .GetIndexes()
            .Single(i => i.Properties.Single().Name == nameof(ApplicationUser.GitHubId));

        index.GetFilter().ShouldBe("[GitHubId] IS NOT NULL");
    }

    private const string HistoryRepositoryTableName = "__EFMigrationsHistory";

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
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
    };

    private static IEnumerable<(string Kind, string Name)> CollectIdentifiers(IRelationalModel model)
    {
        foreach (var table in model.Tables)
        {
            yield return ("table", table.Name);

            foreach (var column in table.Columns)
            {
                yield return ("column", $"{column.Name}");
            }

            foreach (var index in table.Indexes)
            {
                yield return ("index", index.Name);
            }

            foreach (var key in table.UniqueConstraints)
            {
                yield return ("key", key.Name);
            }

            foreach (var foreignKey in table.ForeignKeyConstraints)
            {
                yield return ("foreign key", foreignKey.Name);
            }
        }
    }
}
