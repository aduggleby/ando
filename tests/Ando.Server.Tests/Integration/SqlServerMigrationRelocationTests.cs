// =============================================================================
// SqlServerMigrationRelocationTests.cs
//
// Summary: Verifies that moving the SQL Server migrations to
// Migrations/SqlServer (namespace + [DbContext(typeof(SqlServerAndoDbContext))])
// is a no-op for existing production databases.
//
// The baselines in TestData/Baseline were generated with
// `dotnet ef migrations script` from the migrations BEFORE the move. Each test
// builds a database from such a script, inserts representative data with raw
// SQL, then runs the relocated migrations through SqlServerAndoDbContext.
//
// Design Decisions:
// - Migrates to the last relocated migration (AddBuildGitVersionTag) by name,
//   so later migrations (e.g. NormalizedName) never run here and a failure is
//   always attributable to the relocation itself
// - Row counts and history are read with raw SQL, independent of EF's view
// - The EF insert targets Projects, which later model changes do not touch
// - Two starting points: fully migrated, and an older install (AddBuildProfile)
// - The NormalizedName upgrade (the one later migration that rewrites existing
//   data) has dedicated tests: populated backfill, a collision that must roll
//   back completely, and a Turkish database collation where SQL Server's own
//   UPPER('i') would differ from ToUpperInvariant()
// - Databases with a non-default collation are created with a raw
//   CREATE DATABASE ... COLLATE; the collation comes from a fixed allow-list
// - Skipped when ANDO_TEST_DB_PROVIDERS does not select SqlServer
// =============================================================================

using Ando.Server.Data;
using Ando.Server.Models;
using Ando.Server.Tests.TestFixtures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Proves existing SQL Server databases migrate cleanly after the migration relocation.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerCollection.Name)]
public class SqlServerMigrationRelocationTests
{
    private const string LastRelocatedMigration = "20260219140728_AddBuildGitVersionTag";

    private static readonly string[] RelocatedMigrations =
    [
        "20260130170929_InitialCreate",
        "20260207125546_AddApiTokens",
        "20260207144359_AddBuildProfile",
        "20260219082855_AddSystemSettings",
        LastRelocatedMigration
    ];

    private static readonly string[] DataTables =
    [
        "AspNetUsers", "AspNetRoles", "AspNetUserRoles", "Projects", "ProjectSecrets",
        "Builds", "BuildLogEntries", "ApiTokens"
    ];

    private readonly SqlServerFixture _fixture;

    public SqlServerMigrationRelocationTests(SqlServerFixture fixture)
    {
        Skip.IfNot(DatabaseTestProviders.IsSelected(fixture.Provider), DatabaseTestProviders.SkipReason(fixture.Provider));
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task Migrate_OnFullyMigratedBaseline_IsNoOpAndPreservesData()
    {
        var connectionString = await CreateDatabaseFromBaselineAsync("baseline-full.sql");
        await InsertRepresentativeDataAsync(connectionString, includeSystemSettings: true);

        var tables = DataTables.Append("SystemSettings").ToArray();
        var countsBefore = await GetRowCountsAsync(connectionString, tables);
        var historyBefore = await GetHistoryAsync(connectionString);
        historyBefore.Select(h => h.MigrationId).ShouldBe(RelocatedMigrations);

        await using (var context = CreateContext(connectionString))
        {
            await context.GetService<IMigrator>().MigrateAsync(LastRelocatedMigration);
            await AssertNoRelocatedMigrationPendingAsync(context);
        }

        // History is byte-for-byte unchanged (IDs and recorded product versions)
        (await GetHistoryAsync(connectionString)).ShouldBe(historyBefore);
        (await GetRowCountsAsync(connectionString, tables)).ShouldBe(countsBefore);

        await AssertSampleValuesAsync(connectionString);
        await using (var context = CreateContext(connectionString))
        {
            (await context.SystemSettings.SingleAsync()).AllowUserRegistration.ShouldBeFalse();
        }

        await AssertEfInsertWorksAsync(connectionString);
    }

    [SkippableFact]
    public async Task Migrate_OnAddBuildProfileBaseline_AppliesRemainingMigrationsAndPreservesData()
    {
        var connectionString = await CreateDatabaseFromBaselineAsync("baseline-AddBuildProfile.sql");
        await InsertRepresentativeDataAsync(connectionString, includeSystemSettings: false);

        var countsBefore = await GetRowCountsAsync(connectionString, DataTables);
        var historyBefore = await GetHistoryAsync(connectionString);
        historyBefore.Select(h => h.MigrationId).ShouldBe(RelocatedMigrations.Take(3));
        (await ScalarAsync(connectionString, "SELECT OBJECT_ID(N'SystemSettings')")).ShouldBeNull();

        await using (var context = CreateContext(connectionString))
        {
            await context.GetService<IMigrator>().MigrateAsync(LastRelocatedMigration);
            await AssertNoRelocatedMigrationPendingAsync(context);
        }

        var historyAfter = await GetHistoryAsync(connectionString);
        historyAfter.Select(h => h.MigrationId).ShouldBe(RelocatedMigrations);
        historyAfter.Take(3).ShouldBe(historyBefore);

        (await ScalarAsync(connectionString, "SELECT OBJECT_ID(N'SystemSettings')")).ShouldNotBeNull();
        (await ScalarAsync(connectionString, "SELECT COL_LENGTH('Builds', 'GitVersionTag')")).ShouldNotBeNull();

        (await GetRowCountsAsync(connectionString, DataTables)).ShouldBe(countsBefore);
        await AssertSampleValuesAsync(connectionString);
        await AssertEfInsertWorksAsync(connectionString);
    }

    /// <summary>
    /// The NormalizedName migration is the one post-relocation migration that rewrites existing
    /// data. Runs it against a populated pre-change database with mixed-case secret names across
    /// two projects and checks the backfill, the index swap, and that the new unique index now
    /// rejects case variants.
    /// </summary>
    [SkippableFact]
    public async Task Migrate_OnFullyMigratedBaseline_BackfillsNormalizedNameAndSwapsIndex()
    {
        var connectionString = await CreateDatabaseFromBaselineAsync("baseline-full.sql");
        await InsertRepresentativeDataAsync(connectionString, includeSystemSettings: true);
        await InsertMixedCaseSecretsAsync(connectionString);

        await using (var context = CreateContext(connectionString))
        {
            await context.Database.MigrateAsync();
            (await context.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        }

        var history = await GetHistoryAsync(connectionString);
        history.Select(h => h.MigrationId).Take(RelocatedMigrations.Length).ShouldBe(RelocatedMigrations);
        history.Select(h => h.MigrationId).ShouldContain("20261013075421_AddProjectSecretNormalizedName");

        // Every existing row was backfilled with the invariant uppercase of Name (compared
        // in C#: NormalizedName is binary-collated, Name is not) and nothing was left NULL.
        var secrets = await GetSecretsAsync(connectionString);
        secrets.Count.ShouldBe(4);
        secrets.ShouldAllBe(s => s.NormalizedName == s.Name.ToUpperInvariant());
        (await ScalarAsync(connectionString,
            "SELECT collation_name FROM sys.columns WHERE object_id = OBJECT_ID(N'ProjectSecrets') AND name = N'NormalizedName'"))
            .ShouldBe("Latin1_General_100_BIN2");
        (await ScalarAsync(connectionString,
            "SELECT NormalizedName FROM ProjectSecrets WHERE Name = N'nuget_key' AND ProjectId = (SELECT MIN(Id) FROM Projects)")).ShouldBe("NUGET_KEY");

        // Index swap: the old (ProjectId, Name) index is gone, the new one exists and is unique.
        (await ScalarAsync(connectionString,
            "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_ProjectSecrets_ProjectId_Name'")).ShouldBe(0);
        (await ScalarAsync(connectionString,
            "SELECT is_unique FROM sys.indexes WHERE name = 'IX_ProjectSecrets_ProjectId_NormalizedName'")).ShouldBe(true);

        // A case variant of an existing name is now rejected by the database, same project only.
        await using (var context = CreateContext(connectionString))
        {
            var firstProjectId = await context.Projects.MinAsync(p => p.Id);
            context.ProjectSecrets.Add(new ProjectSecret
            {
                ProjectId = firstProjectId,
                Name = "NUGET_KEY",
                NormalizedName = ProjectSecret.Normalize("NUGET_KEY"),
                EncryptedValue = "x",
                CreatedAt = DateTime.UtcNow
            });
            await Should.ThrowAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }

    /// <summary>
    /// Two legacy secrets that differ only by case (possible under a case-sensitive database
    /// collation) collapse to the same NormalizedName. The migration must stop with error 50001
    /// and roll back completely: no NormalizedName column, the old index still in place, the
    /// history unchanged, and every row and value preserved.
    /// </summary>
    [SkippableFact]
    public async Task Migrate_WithCaseOnlyDuplicateSecrets_FailsAndRollsBackCompletely()
    {
        var connectionString = await CreateDatabaseFromBaselineAsync("baseline-full.sql", "Latin1_General_CS_AS");
        await InsertRepresentativeDataAsync(connectionString, includeSystemSettings: true);
        await InsertSecretsAsync(connectionString, ("a_key", "value-lower"), ("A_KEY", "value-upper"));

        await AssertNormalizedNameMigrationFailsWithDuplicateErrorAsync(connectionString);
        await AssertPreNormalizedNameSchemaIntactAsync(connectionString);

        var secrets = await GetLegacySecretsAsync(connectionString);
        secrets.Count.ShouldBe(3);
        secrets.ShouldContain(("a_key", "value-lower"));
        secrets.ShouldContain(("A_KEY", "value-upper"));
        secrets.ShouldContain(("DEPLOY_TOKEN", "encrypted-value"));
    }

    /// <summary>
    /// Under a Turkish database collation 'i_key' and 'I_KEY' are distinct (Turkish 'i' uppercases
    /// to dotted 'İ'), so both can exist. The application normalizes both to 'I_KEY', so the
    /// migration must treat them as duplicates (fixed-collation backfill) and refuse to upgrade,
    /// leaving the schema untouched.
    /// </summary>
    [SkippableFact]
    public async Task Migrate_TurkishCollation_DottedAndDotlessVariantsCollide_FailsAndRollsBack()
    {
        var connectionString = await CreateDatabaseFromBaselineAsync("baseline-full.sql", "Turkish_CI_AS");
        await InsertRepresentativeDataAsync(connectionString, includeSystemSettings: true);
        await InsertSecretsAsync(connectionString, ("i_key", "value-lower"), ("I_KEY", "value-upper"));

        await AssertNormalizedNameMigrationFailsWithDuplicateErrorAsync(connectionString);
        await AssertPreNormalizedNameSchemaIntactAsync(connectionString);

        var secrets = await GetLegacySecretsAsync(connectionString);
        secrets.ShouldContain(("i_key", "value-lower"));
        secrets.ShouldContain(("I_KEY", "value-upper"));
    }

    /// <summary>
    /// Under a Turkish database collation the backfill still matches ToUpperInvariant():
    /// 'api_key' becomes 'API_KEY' with a plain ASCII 'I', not the dotted 'İ' that the
    /// database's own UPPER would produce, and the application finds it by name.
    /// </summary>
    [SkippableFact]
    public async Task Migrate_TurkishCollation_BackfillsInvariantUppercase()
    {
        var connectionString = await CreateDatabaseFromBaselineAsync("baseline-full.sql", "Turkish_CI_AS");
        await InsertRepresentativeDataAsync(connectionString, includeSystemSettings: true);
        await InsertSecretsAsync(connectionString, ("api_key", "value-api"), ("nuget_key", "value-nuget"));

        // Precondition: this database's own UPPER really is Turkish.
        (await ScalarAsync(connectionString, "SELECT UPPER(N'i')")).ShouldBe("İ");

        await using (var context = CreateContext(connectionString))
        {
            await context.Database.MigrateAsync();
        }

        var secrets = await GetSecretsAsync(connectionString);
        secrets.Single(s => s.Name == "api_key").NormalizedName.ShouldBe("API_KEY");
        secrets.Single(s => s.Name == "nuget_key").NormalizedName.ShouldBe("NUGET_KEY");
        secrets.ShouldAllBe(s => s.NormalizedName == s.Name.ToUpperInvariant());

        await using (var context = CreateContext(connectionString))
        {
            var normalized = ProjectSecret.Normalize("API_KEY");
            var secret = await context.ProjectSecrets.SingleAsync(s => s.NormalizedName == normalized);
            secret.Name.ShouldBe("api_key");
            secret.EncryptedValue.ShouldBe("value-api");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static async Task AssertNormalizedNameMigrationFailsWithDuplicateErrorAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        var exception = await Should.ThrowAsync<SqlException>(() => context.Database.MigrateAsync());
        exception.Number.ShouldBe(50001);
        exception.Message.ShouldContain("duplicate secret names");
    }

    // The state before AddProjectSecretNormalizedName: column absent, old unique index present,
    // new index absent, history exactly the relocated migrations.
    private static async Task AssertPreNormalizedNameSchemaIntactAsync(string connectionString)
    {
        (await ScalarAsync(connectionString, "SELECT COL_LENGTH('ProjectSecrets', 'NormalizedName')")).ShouldBeNull();
        (await ScalarAsync(connectionString,
            "SELECT is_unique FROM sys.indexes WHERE name = 'IX_ProjectSecrets_ProjectId_Name'")).ShouldBe(true);
        (await ScalarAsync(connectionString,
            "SELECT COUNT(*) FROM sys.indexes WHERE name = 'IX_ProjectSecrets_ProjectId_NormalizedName'")).ShouldBe(0);
        (await GetHistoryAsync(connectionString)).Select(h => h.MigrationId).ShouldBe(RelocatedMigrations);

        var tables = DataTables.Append("SystemSettings").ToArray();
        var counts = await GetRowCountsAsync(connectionString, tables);
        counts["AspNetUsers"].ShouldBe(1);
        counts["Projects"].ShouldBe(1);
        counts["Builds"].ShouldBe(1);
        counts["BuildLogEntries"].ShouldBe(1);
        counts["ApiTokens"].ShouldBe(1);
        counts["SystemSettings"].ShouldBe(1);
    }

    // Inserts secrets into the first project using the pre-change schema (no NormalizedName).
    private static async Task InsertSecretsAsync(string connectionString, params (string Name, string Value)[] secrets)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var (name, value) in secrets)
        {
            await using var command = new SqlCommand(
                """
                INSERT INTO ProjectSecrets (ProjectId, Name, EncryptedValue, CreatedAt)
                VALUES ((SELECT MIN(Id) FROM Projects), @name, @value, '2026-01-02T03:04:05');
                """,
                connection);
            command.Parameters.AddWithValue("@name", name);
            command.Parameters.AddWithValue("@value", value);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<List<(string Name, string EncryptedValue)>> GetLegacySecretsAsync(string connectionString)
    {
        var result = new List<(string, string)>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Name, EncryptedValue FROM ProjectSecrets", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }
        return result;
    }

    private static async Task<List<(string Name, string NormalizedName)>> GetSecretsAsync(string connectionString)
    {
        var result = new List<(string, string)>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("SELECT Name, NormalizedName FROM ProjectSecrets", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add((reader.GetString(0), reader.IsDBNull(1) ? null! : reader.GetString(1)));
        }
        return result;
    }

    // Adds a second project and mixed-case secrets so the backfill has more than one row
    // and more than one project to normalize. Uses the pre-change schema (no NormalizedName).
    private static async Task InsertMixedCaseSecretsAsync(string connectionString)
    {
        const string sql = """
            DECLARE @userId int = (SELECT MIN(Id) FROM AspNetUsers);
            DECLARE @firstProjectId int = (SELECT MIN(Id) FROM Projects);
            DECLARE @secondProjectId int;

            INSERT INTO Projects (OwnerId, GitHubRepoId, RepoFullName, RepoUrl, EnablePrBuilds, NotifyOnFailure, CreatedAt)
            VALUES (@userId, 1002, N'acme/second', N'https://github.com/acme/second', 0, 1, '2026-01-02T03:04:05');
            SET @secondProjectId = SCOPE_IDENTITY();

            INSERT INTO ProjectSecrets (ProjectId, Name, EncryptedValue, CreatedAt) VALUES
                (@firstProjectId, N'nuget_key', N'v1', '2026-01-02T03:04:05'),
                (@firstProjectId, N'Other_Key', N'v2', '2026-01-02T03:04:05'),
                (@secondProjectId, N'nuget_key', N'v3', '2026-01-02T03:04:05');
            """;

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static SqlServerAndoDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<SqlServerAndoDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new SqlServerAndoDbContext(options);
    }

    private static async Task AssertNoRelocatedMigrationPendingAsync(SqlServerAndoDbContext context)
    {
        // Every relocated migration is known to the new assembly and applied;
        // anything still pending is a migration added after the relocation.
        var all = context.Database.GetMigrations().ToList();
        all.Take(RelocatedMigrations.Length).ShouldBe(RelocatedMigrations);

        var applied = (await context.Database.GetAppliedMigrationsAsync()).ToList();
        applied.ShouldBe(RelocatedMigrations);

        var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
        pending.ShouldBe(all.Skip(RelocatedMigrations.Length));
    }

    // Collations a test may request; the value is spliced into CREATE DATABASE, so only
    // these known identifiers are accepted.
    private static readonly HashSet<string> AllowedCollations = ["Latin1_General_CS_AS", "Turkish_CI_AS"];

    private async Task<string> CreateDatabaseFromBaselineAsync(string baselineFile, string? collation = null)
    {
        var databaseName = $"AndoRelocation_{Guid.NewGuid():N}";
        var connectionString = _fixture.GetConnectionString(databaseName);

        // Create an empty database (no schema); the baseline script builds the schema.
        if (collation == null)
        {
            await using var context = CreateContext(connectionString);
            await context.GetService<IRelationalDatabaseCreator>().CreateAsync();
        }
        else
        {
            if (!AllowedCollations.Contains(collation))
            {
                throw new ArgumentException($"Collation '{collation}' is not in the allow-list.", nameof(collation));
            }

            await using var master = new SqlConnection(_fixture.GetConnectionString("master"));
            await master.OpenAsync();
            // Identifiers cannot be parameterized; the name is a GUID-derived constant format
            // and the collation comes from the allow-list above.
#pragma warning disable DAP241
            await using var create = new SqlCommand($"CREATE DATABASE [{databaseName}] COLLATE {collation}", master);
#pragma warning restore DAP241
            await create.ExecuteNonQueryAsync();
        }

        var script = ReadBaseline(baselineFile);

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        foreach (var batch in SplitBatches(script))
        {
            await using var command = new SqlCommand(batch, connection) { CommandTimeout = 120 };
            await command.ExecuteNonQueryAsync();
        }

        return connectionString;
    }

    private static string ReadBaseline(string fileName)
    {
        var assembly = typeof(SqlServerMigrationRelocationTests).Assembly;
        using var stream = assembly.GetManifestResourceStream($"Baseline.{fileName}")
            ?? throw new InvalidOperationException($"Embedded baseline '{fileName}' not found.");
        // StreamReader strips the UTF-8 BOM emitted by `dotnet ef migrations script`.
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static IEnumerable<string> SplitBatches(string script)
    {
        var current = new List<string>();
        foreach (var line in script.Split('\n'))
        {
            if (line.Trim().Equals("GO", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Any(l => !string.IsNullOrWhiteSpace(l)))
                {
                    yield return string.Join('\n', current);
                }
                current.Clear();
            }
            else
            {
                current.Add(line);
            }
        }

        if (current.Any(l => !string.IsNullOrWhiteSpace(l)))
        {
            yield return string.Join('\n', current);
        }
    }

    private static async Task InsertRepresentativeDataAsync(string connectionString, bool includeSystemSettings)
    {
        var sql = """
            DECLARE @userId int, @roleId int, @projectId int, @buildId int;

            INSERT INTO AspNetUsers (UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
                EmailVerified, DisplayName, GitHubId, CreatedAt, PhoneNumberConfirmed, TwoFactorEnabled,
                LockoutEnabled, AccessFailedCount, SecurityStamp)
            VALUES (N'relocation@ando.test', N'RELOCATION@ANDO.TEST', N'relocation@ando.test', N'RELOCATION@ANDO.TEST', 1,
                1, N'Relocation User', 4242, '2026-01-02T03:04:05', 0, 0, 1, 0, N'stamp');
            SET @userId = SCOPE_IDENTITY();

            INSERT INTO AspNetRoles (Name, NormalizedName, Description)
            VALUES (N'Admin', N'ADMIN', N'Administrator');
            SET @roleId = SCOPE_IDENTITY();

            INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (@userId, @roleId);

            INSERT INTO Projects (OwnerId, GitHubRepoId, RepoFullName, RepoUrl, EnablePrBuilds, NotifyOnFailure, CreatedAt)
            VALUES (@userId, 1001, N'acme/relocation', N'https://github.com/acme/relocation', 0, 1, '2026-01-02T03:04:05');
            SET @projectId = SCOPE_IDENTITY();

            INSERT INTO ProjectSecrets (ProjectId, Name, EncryptedValue, CreatedAt)
            VALUES (@projectId, N'DEPLOY_TOKEN', N'encrypted-value', '2026-01-02T03:04:05');

            INSERT INTO Builds (ProjectId, CommitSha, Branch, Status, [Trigger], QueuedAt, StepsTotal, StepsCompleted, StepsFailed)
            VALUES (@projectId, N'abc123def456789012345678901234567890abcd', N'main', N'Success', N'Push', '2026-01-02T03:04:05', 3, 3, 0);
            SET @buildId = SCOPE_IDENTITY();

            INSERT INTO BuildLogEntries (BuildId, Sequence, Type, Message, Timestamp)
            VALUES (@buildId, 1, N'Info', N'relocation log line', '2026-01-02T03:04:06');

            INSERT INTO ApiTokens (UserId, Name, Prefix, TokenHash, CreatedAt)
            VALUES (@userId, N'ci', N'ando_abc', N'hash-value', '2026-01-02T03:04:05');
            """;

        if (includeSystemSettings)
        {
            sql += "\nINSERT INTO SystemSettings (Id, AllowUserRegistration, UpdatedAt) VALUES (1, 0, '2026-01-02T03:04:05');";
        }

        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertSampleValuesAsync(string connectionString)
    {
        (await ScalarAsync(connectionString, "SELECT Email FROM AspNetUsers")).ShouldBe("relocation@ando.test");
        (await ScalarAsync(connectionString, "SELECT Name FROM ProjectSecrets")).ShouldBe("DEPLOY_TOKEN");
        (await ScalarAsync(connectionString, "SELECT Message FROM BuildLogEntries")).ShouldBe("relocation log line");
        (await ScalarAsync(connectionString, "SELECT Prefix FROM ApiTokens")).ShouldBe("ando_abc");

        await using var context = CreateContext(connectionString);
        var project = await context.Projects.AsNoTracking().SingleAsync();
        project.RepoFullName.ShouldBe("acme/relocation");
        project.DefaultBranch.ShouldBe("main");
        project.TimeoutMinutes.ShouldBe(15);

        var build = await context.Builds.AsNoTracking().SingleAsync();
        build.Status.ShouldBe(BuildStatus.Success);
        build.Trigger.ShouldBe(BuildTrigger.Push);

        var user = await context.Users.AsNoTracking().SingleAsync();
        user.GitHubId.ShouldBe(4242);
    }

    private static async Task AssertEfInsertWorksAsync(string connectionString)
    {
        await using var context = CreateContext(connectionString);
        var ownerId = await context.Users.Select(u => u.Id).SingleAsync();

        context.Projects.Add(new Project
        {
            OwnerId = ownerId,
            GitHubRepoId = 2002,
            RepoFullName = "acme/after-relocation",
            RepoUrl = "https://github.com/acme/after-relocation",
            CreatedAt = DateTime.UtcNow
        });
        await context.SaveChangesAsync();

        (await context.Projects.CountAsync()).ShouldBe(2);
    }

    private static async Task<List<(string MigrationId, string ProductVersion)>> GetHistoryAsync(string connectionString)
    {
        var result = new List<(string, string)>();
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            "SELECT MigrationId, ProductVersion FROM __EFMigrationsHistory ORDER BY MigrationId", connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            result.Add((reader.GetString(0), reader.GetString(1)));
        }
        return result;
    }

    private static async Task<Dictionary<string, int>> GetRowCountsAsync(string connectionString, IEnumerable<string> tables)
    {
        var counts = new Dictionary<string, int>();
        foreach (var table in tables)
        {
            var value = await ScalarAsync(connectionString, $"SELECT COUNT(*) FROM [{table}]");
            counts[table] = Convert.ToInt32(value);
        }
        return counts;
    }

    private static async Task<object?> ScalarAsync(string connectionString, string sql)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is DBNull ? null : value;
    }
}
