// =============================================================================
// DataPortabilityTestData.cs
//
// Summary: Shared helpers for the data export/import integration tests:
// migrated throwaway databases, a representative seed, and a provider-neutral
// snapshot used to compare source and target after a round trip.
//
// Design Decisions:
// - Databases are created with MigrateAsync (the real migration sets), like a
//   production instance, and dropped afterwards
// - The seed covers every exported table, every BuildStatus, gaps in identity
//   values (deleted rows), a non-zero LockoutEnd, AES-encrypted secrets and
//   2,000 log entries. Timestamps are whole microseconds so the SQL Server
//   datetime2(7) -> PostgreSQL timestamptz(6) direction is lossless. It also
//   holds values equal to the CLR default on columns with a different store
//   default (TimeoutMinutes 0, DefaultBranch "", AllowUserRegistration false)
// - The snapshot hashes every column of every row (a superset of the key
//   checksum) with an independent canonical form (DateTime ticks + Kind,
//   DateTimeOffset UTC ticks + offset), not the export serializer
// - The expected target snapshot applies the import rule for Queued/Running
//   builds to the source rows before hashing
// =============================================================================

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.DataPortability;
using Ando.Server.Models;
using Ando.Server.Services;
using Ando.Server.Tests.TestFixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// A migrated, uniquely named database on one provider, dropped on dispose.
/// </summary>
internal sealed class PortabilityTestDatabase : IAsyncDisposable
{
    private PortabilityTestDatabase(DatabaseProvider provider, string connectionString)
    {
        Provider = provider;
        ConnectionString = connectionString;
    }

    /// <summary>
    /// Provider of the database.
    /// </summary>
    public DatabaseProvider Provider { get; }

    /// <summary>
    /// Connection string to the database.
    /// </summary>
    public string ConnectionString { get; }

    /// <summary>
    /// Creates a new database on the fixture's server and applies all migrations.
    /// </summary>
    /// <param name="fixture">Container fixture.</param>
    /// <returns>The migrated database.</returns>
    public static async Task<PortabilityTestDatabase> CreateMigratedAsync(IDatabaseFixture fixture)
    {
        var database = new PortabilityTestDatabase(
            fixture.Provider, fixture.GetConnectionString($"ando_port_{Guid.NewGuid():N}"));
        await using var db = database.CreateContext();
        await db.Database.MigrateAsync();
        return database;
    }

    /// <summary>
    /// Creates a new context (the provider-specific subclass, as the server registers it).
    /// </summary>
    /// <returns>A new context; the caller disposes it.</returns>
    public AndoDbContext CreateContext() => Provider switch
    {
        DatabaseProvider.SqlServer => new SqlServerAndoDbContext(
            new DbContextOptionsBuilder<SqlServerAndoDbContext>().UseSqlServer(ConnectionString).Options),
        DatabaseProvider.PostgreSql => new PostgreSqlAndoDbContext(
            new DbContextOptionsBuilder<PostgreSqlAndoDbContext>().UseNpgsql(ConnectionString).Options),
        _ => throw new InvalidOperationException($"Unsupported provider {Provider}.")
    };

    /// <summary>
    /// Drops the database.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        await using var db = CreateContext();
        await db.Database.EnsureDeletedAsync();
    }
}

/// <summary>
/// Provider-neutral picture of all exported tables.
/// </summary>
/// <param name="Counts">Rows per table.</param>
/// <param name="Checksums">SHA-256 over the canonical form of all rows, per table.</param>
/// <param name="MaxIds">Largest identity value per identity table.</param>
/// <param name="NonUtcDateTimes">Number of DateTime values read back with a Kind other than Utc.</param>
internal sealed record PortabilitySnapshot(
    IReadOnlyDictionary<string, long> Counts,
    IReadOnlyDictionary<string, string> Checksums,
    IReadOnlyDictionary<string, long> MaxIds,
    int NonUtcDateTimes);

/// <summary>
/// Seed, snapshot and round-trip helpers for the data portability tests.
/// </summary>
internal static class DataPortabilityTestData
{
    /// <summary>
    /// Key used to encrypt the seeded secrets (32 bytes, base64).
    /// </summary>
    public static readonly string SourceKey = Convert.ToBase64String(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray());

    /// <summary>
    /// A different valid key, for the wrong-key test.
    /// </summary>
    public static readonly string OtherKey = Convert.ToBase64String(Enumerable.Range(101, 32).Select(i => (byte)i).ToArray());

    /// <summary>
    /// Number of seeded build log entries.
    /// </summary>
    public const int LogEntryCount = 2000;

    /// <summary>
    /// Seeded LockoutEnd of the admin user (microsecond precision, offset zero as Identity writes it).
    /// </summary>
    public static readonly DateTimeOffset LockoutEnd = new DateTimeOffset(2031, 5, 6, 7, 8, 9, TimeSpan.Zero).AddTicks(1_234_560);

    /// <summary>
    /// Plaintext of the seeded secrets by (repo, secret name).
    /// </summary>
    public static readonly IReadOnlyDictionary<(string Repo, string Name), string> SecretPlaintexts =
        new Dictionary<(string, string), string>
        {
            [("acme/app", "API_KEY")] = "s3cr3t-äöü-🔑",
            [("acme/app", "Db_Password")] = "p@ss word\nwith newline",
            [("acme/lib", "TOKEN")] = "ghp_" + new string('x', 36)
        };

    private static readonly DateTime BaseTime = new DateTime(2026, 3, 4, 5, 6, 7, DateTimeKind.Utc).AddTicks(1_234_560);

    /// <summary>
    /// Creates an encryption service for <paramref name="key"/>.
    /// </summary>
    /// <param name="key">Base64 AES-256 key.</param>
    /// <returns>The real encryption service.</returns>
    public static IEncryptionService Encryption(string key) =>
        new EncryptionService(Options.Create(new EncryptionSettings { Key = key }));

    /// <summary>
    /// Seeds every exported table through EF. Deletes some rows afterwards so identity values have gaps.
    /// </summary>
    /// <param name="database">The migrated source database.</param>
    public static async Task SeedAsync(PortabilityTestDatabase database)
    {
        var encryption = Encryption(SourceKey);
        await using var db = database.CreateContext();

        var admin = new ApplicationRole { Name = UserRoles.Admin, NormalizedName = "ADMIN", Description = "Administrators", ConcurrencyStamp = "r1" };
        var user = new ApplicationRole { Name = UserRoles.User, NormalizedName = "USER", Description = "Users", ConcurrencyStamp = "r2" };
        var doomedRole = new ApplicationRole { Name = "Temp", NormalizedName = "TEMP" };
        db.Roles.AddRange(admin, doomedRole, user);
        await db.SaveChangesAsync();

        var alice = NewUser("alice@example.com", "Alice");
        alice.LockoutEnabled = true;
        alice.LockoutEnd = LockoutEnd;
        alice.AccessFailedCount = 3;
        alice.EmailVerified = true;
        alice.GitHubId = 123456789012;
        alice.GitHubLogin = "alice";
        alice.GitHubConnectedAt = BaseTime.AddDays(-3);
        alice.LastLoginAt = BaseTime.AddHours(-1);
        alice.TwoFactorEnabled = false;
        var doomedUser = NewUser("gone@example.com", "Gone");
        var bob = NewUser("bob@example.com", null);
        bob.EmailVerificationToken = "verify-token";
        bob.EmailVerificationSentAt = BaseTime.AddMinutes(5);
        db.Users.AddRange(alice, doomedUser, bob);
        await db.SaveChangesAsync();

        db.UserRoles.AddRange(
            new IdentityUserRole<int> { UserId = alice.Id, RoleId = admin.Id },
            new IdentityUserRole<int> { UserId = alice.Id, RoleId = user.Id },
            new IdentityUserRole<int> { UserId = bob.Id, RoleId = user.Id });
        db.UserClaims.AddRange(
            new IdentityUserClaim<int> { UserId = alice.Id, ClaimType = "team", ClaimValue = "core" },
            new IdentityUserClaim<int> { UserId = bob.Id, ClaimType = "team", ClaimValue = "web" });
        db.RoleClaims.Add(new IdentityRoleClaim<int> { RoleId = admin.Id, ClaimType = "permission", ClaimValue = "all" });
        db.UserLogins.Add(new IdentityUserLogin<int> { LoginProvider = "GitHub", ProviderKey = "gh-1", ProviderDisplayName = "GitHub", UserId = alice.Id });
        db.UserTokens.Add(new IdentityUserToken<int> { UserId = alice.Id, LoginProvider = "[AspNetUserStore]", Name = "AuthenticatorKey", Value = "ABCDEF" });

        var app = new Project
        {
            OwnerId = alice.Id, GitHubRepoId = 9_000_000_001, RepoFullName = "acme/app", RepoUrl = "https://github.com/acme/app",
            DefaultBranch = "main", BranchFilter = "main,release/*", InstallationId = 77, EnablePrBuilds = true,
            TimeoutMinutes = 30, DockerImage = "mcr.microsoft.com/dotnet/sdk:9.0", RequiredSecrets = "API_KEY,Db_Password",
            Profile = "ci", AvailableProfiles = "ci,release", NotifyOnFailure = false, NotificationEmail = "ops@example.com",
            CreatedAt = BaseTime, LastBuildAt = BaseTime.AddHours(2)
        };
        var lib = new Project
        {
            OwnerId = bob.Id, GitHubRepoId = 9_000_000_002, RepoFullName = "acme/lib", RepoUrl = "https://github.com/acme/lib",
            DefaultBranch = "", TimeoutMinutes = 0, CreatedAt = BaseTime.AddDays(1)
        };
        db.Projects.AddRange(app, lib);
        await db.SaveChangesAsync();

        // EF omits a value equal to the CLR default when the column has a store default, so
        // force the values the import must preserve (store defaults: 15, "main").
        await db.Projects.Where(p => p.Id == lib.Id).ExecuteUpdateAsync(s => s
            .SetProperty(p => p.TimeoutMinutes, 0)
            .SetProperty(p => p.DefaultBranch, ""));

        foreach (var ((repo, name), plaintext) in SecretPlaintexts)
        {
            db.ProjectSecrets.Add(new ProjectSecret
            {
                ProjectId = repo == "acme/app" ? app.Id : lib.Id,
                Name = name,
                NormalizedName = ProjectSecret.Normalize(name),
                EncryptedValue = encryption.Encrypt(plaintext),
                CreatedAt = BaseTime.AddMinutes(1),
                UpdatedAt = name == "TOKEN" ? BaseTime.AddMinutes(2) : null
            });
        }

        var builds = Enum.GetValues<BuildStatus>().Select((status, i) => new Build
        {
            ProjectId = app.Id,
            CommitSha = $"{i:x2}".PadRight(40, 'a'),
            Branch = "main",
            CommitMessage = $"Commit {i} – ünïcode",
            CommitAuthor = "Alice",
            PullRequestNumber = i == 2 ? 42 : null,
            GitVersionTag = i == 2 ? "v1.2.3" : null,
            Profile = "ci",
            Status = status,
            Trigger = i % 2 == 0 ? BuildTrigger.Push : BuildTrigger.Manual,
            QueuedAt = BaseTime.AddMinutes(10 + i),
            StartedAt = status == BuildStatus.Queued ? null : BaseTime.AddMinutes(11 + i),
            FinishedAt = status is BuildStatus.Queued or BuildStatus.Running ? null : BaseTime.AddMinutes(20 + i),
            Duration = status is BuildStatus.Queued or BuildStatus.Running ? null : new TimeSpan(0, 1, 2, 3).Add(TimeSpan.FromTicks(4_567_890)),
            StepsTotal = 5,
            StepsCompleted = i,
            StepsFailed = status == BuildStatus.Failed ? 1 : 0,
            ErrorMessage = status == BuildStatus.Failed ? "Step failed" : null,
            HangfireJobId = status is BuildStatus.Queued or BuildStatus.Running ? $"job-{i}" : null
        }).ToList();
        var doomedBuild = new Build { ProjectId = lib.Id, CommitSha = new string('f', 40), Branch = "dev", Status = BuildStatus.Success, Trigger = BuildTrigger.Push, QueuedAt = BaseTime };
        db.Builds.AddRange(builds.Take(3));
        db.Builds.Add(doomedBuild);
        db.Builds.AddRange(builds.Skip(3));
        await db.SaveChangesAsync();

        var success = builds.Single(b => b.Status == BuildStatus.Success);
        var failed = builds.Single(b => b.Status == BuildStatus.Failed);
        var types = Enum.GetValues<LogEntryType>();
        for (var i = 0; i < LogEntryCount; i++)
        {
            db.BuildLogEntries.Add(new BuildLogEntry
            {
                BuildId = i < 1500 ? success.Id : failed.Id,
                Sequence = i,
                Type = types[i % types.Length],
                Message = $"line {i}: \"quoted\" \t tab – ünïcode {new string('#', i % 50)}",
                StepName = i % 3 == 0 ? null : $"step-{i % 7}",
                Timestamp = BaseTime.AddTicks(i * 10_010L)
            });
        }

        db.BuildArtifacts.AddRange(
            new BuildArtifact { BuildId = success.Id, Name = "app.zip", StoragePath = "1/app.zip", SizeBytes = 5_000_000_000, CreatedAt = BaseTime, ExpiresAt = BaseTime.AddDays(30) },
            new BuildArtifact { BuildId = success.Id, Name = "symbols.zip", StoragePath = "1/symbols.zip", SizeBytes = 12, CreatedAt = BaseTime, ExpiresAt = BaseTime.AddDays(7) });

        db.ApiTokens.AddRange(
            new ApiToken { UserId = alice.Id, Name = "CI", Prefix = "ando_ab12", TokenHash = "hash1", CreatedAt = BaseTime, LastUsedAt = BaseTime.AddHours(5) },
            new ApiToken { UserId = bob.Id, Name = "Old", Prefix = "ando_cd34", TokenHash = "hash2", CreatedAt = BaseTime, RevokedAt = BaseTime.AddDays(2) });

        db.SystemSettings.Add(new SystemSettings { Id = 1, AllowUserRegistration = false, UpdatedAt = BaseTime });
        await db.SaveChangesAsync();
        await db.SystemSettings.ExecuteUpdateAsync(s => s.SetProperty(x => x.AllowUserRegistration, false)); // store default: true

        // Gaps in identity values: the import must keep IDs, not renumber.
        db.Roles.Remove(doomedRole);
        db.Users.Remove(doomedUser);
        db.Builds.Remove(doomedBuild);
        await db.SaveChangesAsync();
    }

    private static ApplicationUser NewUser(string email, string? displayName) => new()
    {
        UserName = email,
        NormalizedUserName = email.ToUpperInvariant(),
        Email = email,
        NormalizedEmail = email.ToUpperInvariant(),
        EmailConfirmed = true,
        PasswordHash = "AQAAAAIAAYagAAAAE" + email.Length,
        SecurityStamp = "stamp-" + email,
        ConcurrencyStamp = "cc-" + email,
        DisplayName = displayName,
        AvatarUrl = displayName == null ? null : "https://example.com/a.png",
        CreatedAt = BaseTime.AddDays(-10)
    };

    /// <summary>
    /// Inserts the roles a freshly started server seeds (Admin, User), as on a real target.
    /// </summary>
    /// <param name="database">The target database.</param>
    public static async Task SeedStartupRolesAsync(PortabilityTestDatabase database)
    {
        await using var db = database.CreateContext();
        db.Roles.AddRange(
            new ApplicationRole { Name = UserRoles.Admin, NormalizedName = "ADMIN" },
            new ApplicationRole { Name = UserRoles.User, NormalizedName = "USER" });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Takes a snapshot of every exported table.
    /// </summary>
    /// <param name="database">The database.</param>
    /// <param name="cancelledAtUtc">
    /// When set, rows are transformed by the import rule for Queued/Running builds (using this
    /// as the fallback FinishedAt) before hashing, giving the expected target snapshot.
    /// </param>
    /// <returns>The snapshot.</returns>
    public static async Task<PortabilitySnapshot> SnapshotAsync(PortabilityTestDatabase database, DateTime? cancelledAtUtc = null)
    {
        await using var db = database.CreateContext();
        var counts = new Dictionary<string, long>();
        var checksums = new Dictionary<string, string>();
        var maxIds = new Dictionary<string, long>();
        var nonUtc = 0;

        foreach (var table in PortableTables.Resolve(db.Model))
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            long rows = 0;
            long maxId = 0;
            await foreach (var entity in table.QueryOrdered(db, CancellationToken.None))
            {
                if (cancelledAtUtc.HasValue && entity is Build { Status: BuildStatus.Queued or BuildStatus.Running } build)
                {
                    build.Status = BuildStatus.Cancelled;
                    build.HangfireJobId = null;
                    build.ErrorMessage = DataImportService.CancelledByImportMessage;
                    build.FinishedAt ??= cancelledAtUtc.Value;
                }

                var line = new StringBuilder();
                foreach (var property in table.Properties)
                {
                    var value = property.GetGetter().GetClrValue(entity);
                    if (value is DateTime { Kind: not DateTimeKind.Utc })
                    {
                        nonUtc++;
                    }

                    line.Append(property.Name).Append('=').Append(Canonical(value)).Append('|');
                }

                hash.AppendData(Encoding.UTF8.GetBytes(line.Append('\n').ToString()));
                if (table.IdentityKey != null)
                {
                    maxId = Math.Max(maxId, Convert.ToInt64(table.IdentityKey.GetGetter().GetClrValue(entity), CultureInfo.InvariantCulture));
                }

                rows++;
            }

            counts[table.Name] = rows;
            checksums[table.Name] = Convert.ToHexString(hash.GetHashAndReset());
            if (table.IdentityKey != null)
            {
                maxIds[table.Name] = maxId;
            }
        }

        return new PortabilitySnapshot(counts, checksums, maxIds, nonUtc);
    }

    private static string Canonical(object? value) => value switch
    {
        null => "<null>",
        DateTime dt => $"{dt.Ticks}:{dt.Kind}",
        DateTimeOffset dto => $"{dto.UtcTicks}{dto.Offset}",
        TimeSpan ts => ts.Ticks.ToString(CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? ""
    };

    /// <summary>
    /// Exports <paramref name="source"/> to a new file and returns the result.
    /// </summary>
    /// <param name="source">Source database.</param>
    /// <param name="path">Output file.</param>
    /// <returns>The export result.</returns>
    public static async Task<DataExportResult> ExportAsync(PortabilityTestDatabase source, string path)
    {
        await using var db = source.CreateContext();
        return await new DataExportService(db).ExportAsync(path, overwrite: false);
    }

    /// <summary>
    /// Imports <paramref name="path"/> into <paramref name="target"/>.
    /// </summary>
    /// <param name="target">Target database.</param>
    /// <param name="path">Export file.</param>
    /// <param name="force">Pass --force.</param>
    /// <param name="key">Encryption key of the target instance.</param>
    /// <returns>The import result.</returns>
    public static async Task<DataImportResult> ImportAsync(
        PortabilityTestDatabase target, string path, bool force = false, string? key = null)
    {
        await using var db = target.CreateContext();
        return await new DataImportService(db, () => Encryption(key ?? SourceKey)).ImportAsync(path, force);
    }

    /// <summary>
    /// Full round trip: seed source, export, import into a target that has the startup roles,
    /// and assert that the target equals the source (with the Queued/Running rule applied).
    /// </summary>
    /// <param name="sourceFixture">Fixture of the source provider.</param>
    /// <param name="targetFixture">Fixture of the target provider.</param>
    public static async Task AssertRoundTripAsync(IDatabaseFixture sourceFixture, IDatabaseFixture targetFixture)
    {
        var directory = Directory.CreateTempSubdirectory("ando-portability-");
        await using var source = await PortabilityTestDatabase.CreateMigratedAsync(sourceFixture);
        await using var target = await PortabilityTestDatabase.CreateMigratedAsync(targetFixture);
        try
        {
            await SeedAsync(source);
            await SeedStartupRolesAsync(target);
            var path = Path.Combine(directory.FullName, "export.jsonl");

            var export = await ExportAsync(source, path);
            export.Manifest.SourceProvider.ShouldBe(sourceFixture.Provider.ToString());
            export.RowCounts.Single(c => c.Key == "BuildLogEntries").Value.ShouldBe(LogEntryCount);

            var import = await ImportAsync(target, path);

            var expected = await SnapshotAsync(source, export.Manifest.ExportedAtUtc);
            var actual = await SnapshotAsync(target);

            // Counts per table, equal to the export and to what the import reported.
            actual.Counts.ShouldBe(expected.Counts);
            import.RowCounts.ShouldBe(export.RowCounts);
            import.CancelledBuilds.ShouldBe(2);
            import.SecretsVerified.ShouldBe(SecretPlaintexts.Count);
            import.DeletedRows.ShouldBe(2); // the target's startup roles

            // Every column of every row, IDs included.
            foreach (var table in PortableTables.TableNames)
            {
                actual.Checksums[table].ShouldBe(expected.Checksums[table], $"checksum of {table}");
            }

            actual.MaxIds.ShouldBe(expected.MaxIds);
            actual.NonUtcDateTimes.ShouldBe(0);

            await AssertTargetDetailsAsync(source, target, actual);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static async Task AssertTargetDetailsAsync(
        PortabilityTestDatabase source, PortabilityTestDatabase target, PortabilitySnapshot imported)
    {
        await using var sourceDb = source.CreateContext();
        await using (var db = target.CreateContext())
        {
            // IDs preserved, including the gaps left by deleted rows.
            (await db.Roles.OrderBy(r => r.Id).Select(r => r.Id).ToListAsync())
                .ShouldBe(await sourceDb.Roles.OrderBy(r => r.Id).Select(r => r.Id).ToListAsync());
            (await db.Builds.OrderBy(b => b.Id).Select(b => b.Id).ToListAsync())
                .ShouldBe(await sourceDb.Builds.OrderBy(b => b.Id).Select(b => b.Id).ToListAsync());

            var alice = await db.Users.AsNoTracking().SingleAsync(u => u.Email == "alice@example.com");
            alice.LockoutEnd.ShouldBe(LockoutEnd);
            alice.CreatedAt.Kind.ShouldBe(DateTimeKind.Utc);
            alice.CreatedAt.ShouldBe((await sourceDb.Users.AsNoTracking().SingleAsync(u => u.Email == "alice@example.com")).CreatedAt);

            // Values equal to the CLR default whose column has a different store default survive.
            (await sourceDb.SystemSettings.AsNoTracking().SingleAsync()).AllowUserRegistration.ShouldBeFalse();
            var settings = await db.SystemSettings.AsNoTracking().SingleAsync();
            settings.AllowUserRegistration.ShouldBeFalse();
            var sourceLib = await sourceDb.Projects.AsNoTracking().SingleAsync(p => p.RepoFullName == "acme/lib");
            sourceLib.TimeoutMinutes.ShouldBe(0);
            sourceLib.DefaultBranch.ShouldBe("");
            var lib = await db.Projects.AsNoTracking().SingleAsync(p => p.RepoFullName == "acme/lib");
            lib.TimeoutMinutes.ShouldBe(0);
            lib.DefaultBranch.ShouldBe("");
            lib.BranchFilter.ShouldBe("main,master");

            var cancelled = await db.Builds.AsNoTracking()
                .Where(b => b.ErrorMessage == DataImportService.CancelledByImportMessage).ToListAsync();
            cancelled.Count.ShouldBe(2);
            cancelled.ShouldAllBe(b => b.Status == BuildStatus.Cancelled && b.HangfireJobId == null && b.FinishedAt != null);
            (await db.Builds.CountAsync(b => b.Status == BuildStatus.Queued || b.Status == BuildStatus.Running)).ShouldBe(0);

            var log = await db.BuildLogEntries.AsNoTracking().OrderBy(l => l.Id).FirstAsync();
            log.Timestamp.Kind.ShouldBe(DateTimeKind.Utc);

            // Secrets decrypt to the same plaintext with the shared key.
            var encryption = Encryption(SourceKey);
            var secrets = await db.ProjectSecrets.AsNoTracking()
                .Select(s => new { s.Project.RepoFullName, s.Name, s.EncryptedValue }).ToListAsync();
            secrets.Count.ShouldBe(SecretPlaintexts.Count);
            foreach (var secret in secrets)
            {
                encryption.Decrypt(secret.EncryptedValue).ShouldBe(SecretPlaintexts[(secret.RepoFullName, secret.Name)]);
            }
        }

        // New rows get identity values above the imported maximum (sequences/identity seeds moved).
        await using (var db = target.CreateContext())
        {
            var owner = await db.Users.OrderBy(u => u.Id).FirstAsync();
            var role = new ApplicationRole { Name = "New", NormalizedName = "NEW" };
            var user = NewUser("new@example.com", "New");
            var project = new Project { OwnerId = owner.Id, GitHubRepoId = 1, RepoFullName = "acme/new", RepoUrl = "https://github.com/acme/new", CreatedAt = DateTime.UtcNow };
            db.AddRange(role, user, project);
            await db.SaveChangesAsync();

            var build = new Build { ProjectId = project.Id, CommitSha = new string('b', 40), Branch = "main", Trigger = BuildTrigger.Manual, QueuedAt = DateTime.UtcNow };
            db.Builds.Add(build);
            await db.SaveChangesAsync();
            var log = new BuildLogEntry { BuildId = build.Id, Sequence = 0, Type = LogEntryType.Info, Message = "after import", Timestamp = DateTime.UtcNow };
            var token = new ApiToken { UserId = owner.Id, Name = "New", Prefix = "ando_new", TokenHash = "h", CreatedAt = DateTime.UtcNow };
            db.AddRange(log, token);
            await db.SaveChangesAsync();

            role.Id.ShouldBeGreaterThan((int)imported.MaxIds["AspNetRoles"]);
            user.Id.ShouldBeGreaterThan((int)imported.MaxIds["AspNetUsers"]);
            project.Id.ShouldBeGreaterThan((int)imported.MaxIds["Projects"]);
            build.Id.ShouldBeGreaterThan((int)imported.MaxIds["Builds"]);
            log.Id.ShouldBeGreaterThan(imported.MaxIds["BuildLogEntries"]);
            token.Id.ShouldBeGreaterThan((int)imported.MaxIds["ApiTokens"]);
        }
    }
}
