// =============================================================================
// MigrationSchemaTestsBase.cs
//
// Summary: Applies a provider's real migration set to a fresh database and
// runs a behavioral pass (constraints, cascades, timestamps, Identity,
// defaults, seeding) against the migrated schema.
//
// Every other database-backed test builds its schema with EnsureCreated, which
// derives the schema from the model and never runs a migration. These tests
// are the only place the hand-maintained migration sets (Migrations/SqlServer,
// Migrations/PostgreSql) are exercised end to end, per provider.
//
// Design Decisions:
// - One database per test instance (unique name), migrated with MigrateAsync
//   and dropped in DisposeAsync, so tests are independent
// - No schema-comparison tool: the behavioral pass is the check
// - Cascade deletes go through ExecuteDeleteAsync so the database's foreign
//   keys do the work, not EF's client-side cascade of tracked entities
// - Reads always use a fresh scope so values come from the database, not the
//   change tracker
// - Identity is wired with AddIdentityCore against the same AndoDbContext
//   registration the application uses (AndoDbContext -> provider subclass)
// =============================================================================

using Ando.Server.BuildExecution;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Hubs;
using Ando.Server.Infrastructure.Startup;
using Ando.Server.Models;
using Ando.Server.Tests.TestFixtures;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Migration and behavioral tests shared by all database providers.
/// </summary>
[Trait("Category", "Integration")]
public abstract class MigrationSchemaTestsBase : IAsyncLifetime
{
    private readonly DatabaseProvider _provider;
    private readonly ServiceProvider _services;

    /// <summary>
    /// Wires EF and Identity against a new, uniquely named database, or skips
    /// the test when the fixture's provider is not selected.
    /// </summary>
    /// <param name="fixture">Container fixture for the provider under test.</param>
    protected MigrationSchemaTestsBase(IDatabaseFixture fixture)
    {
        Skip.IfNot(DatabaseTestProviders.IsSelected(fixture.Provider), DatabaseTestProviders.SkipReason(fixture.Provider));

        _provider = fixture.Provider;
        var connectionString = fixture.GetConnectionString($"ando_schema_{Guid.NewGuid():N}");

        var services = new ServiceCollection();
        services.AddLogging();
        switch (_provider)
        {
            case DatabaseProvider.SqlServer:
                services.AddDbContext<AndoDbContext, SqlServerAndoDbContext>(o => o.UseSqlServer(connectionString));
                break;
            case DatabaseProvider.PostgreSql:
                services.AddDbContext<AndoDbContext, PostgreSqlAndoDbContext>(o => o.UseNpgsql(connectionString));
                break;
            default:
                throw new InvalidOperationException($"Unsupported database provider '{_provider}'.");
        }

        services.AddIdentityCore<ApplicationUser>(options => options.User.RequireUniqueEmail = true)
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<AndoDbContext>();

        _services = services.BuildServiceProvider();
    }

    /// <summary>
    /// Creates the database by applying the provider's migrations (not EnsureCreated).
    /// </summary>
    public async Task InitializeAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        await Db(scope).Database.MigrateAsync();
    }

    /// <summary>
    /// Drops the test database.
    /// </summary>
    public async Task DisposeAsync()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            await Db(scope).Database.EnsureDeletedAsync();
        }

        await _services.DisposeAsync();
    }

    // -------------------------------------------------------------------------
    // Migrations
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task Migrate_AppliesEveryMigration_AndMatchesModel()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);

        (await db.Database.GetPendingMigrationsAsync()).ShouldBeEmpty();
        (await db.Database.GetAppliedMigrationsAsync()).ShouldBe(db.Database.GetMigrations());
        db.Database.HasPendingModelChanges().ShouldBeFalse();
    }

    // -------------------------------------------------------------------------
    // Constraints and cascades
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task SecretNames_AreUniquePerProject_IgnoringCase()
    {
        var (_, projectA) = await CreateProjectAsync();
        var (_, projectB) = await CreateProjectAsync();

        await AddSecretAsync(projectA.Id, "A_KEY");

        // Same name in another project is allowed.
        await AddSecretAsync(projectB.Id, "a_key");

        // Differing only in case within one project violates (ProjectId, NormalizedName).
        await Should.ThrowAsync<DbUpdateException>(() => AddSecretAsync(projectA.Id, "a_key"));

        await using var scope = _services.CreateAsyncScope();
        (await Db(scope).ProjectSecrets.CountAsync(s => s.ProjectId == projectA.Id)).ShouldBe(1);
        (await Db(scope).ProjectSecrets.CountAsync(s => s.ProjectId == projectB.Id)).ShouldBe(1);
    }

    [SkippableFact]
    public async Task DeletingProject_CascadesToBuildsLogsSecretsAndArtifacts()
    {
        var (_, project) = await CreateProjectAsync();
        var buildId = await CreateBuildAsync(project.Id);
        await AddSecretAsync(project.Id, "TOKEN");

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope);
            db.BuildLogEntries.Add(new BuildLogEntry
            {
                BuildId = buildId,
                Sequence = 1,
                Type = LogEntryType.Info,
                Message = "hello",
                Timestamp = DateTime.UtcNow
            });
            db.BuildArtifacts.Add(new BuildArtifact
            {
                BuildId = buildId,
                Name = "out.zip",
                StoragePath = "1/out.zip",
                SizeBytes = 42,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(1)
            });
            await db.SaveChangesAsync();
        }

        // Delete in the database only, so the schema's ON DELETE CASCADE does the work.
        await using (var scope = _services.CreateAsyncScope())
        {
            (await Db(scope).Projects.Where(p => p.Id == project.Id).ExecuteDeleteAsync()).ShouldBe(1);
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope);
            (await db.Builds.CountAsync(b => b.ProjectId == project.Id)).ShouldBe(0);
            (await db.BuildLogEntries.CountAsync(e => e.BuildId == buildId)).ShouldBe(0);
            (await db.BuildArtifacts.CountAsync(a => a.BuildId == buildId)).ShouldBe(0);
            (await db.ProjectSecrets.CountAsync(s => s.ProjectId == project.Id)).ShouldBe(0);
        }
    }

    // -------------------------------------------------------------------------
    // Timestamps
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task Timestamps_RoundTripAsUtc_AndNullsStayNull()
    {
        var (_, project) = await CreateProjectAsync();
        var unspecified = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Unspecified);
        var local = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Local);

        int buildId;
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope);
            var build = NewBuild(project.Id);
            build.QueuedAt = unspecified;
            build.StartedAt = local;
            build.FinishedAt = null;
            db.Builds.Add(build);
            await db.SaveChangesAsync();
            buildId = build.Id;
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var build = await Db(scope).Builds.AsNoTracking().SingleAsync(b => b.Id == buildId);

            // Unspecified is taken as UTC without shifting.
            build.QueuedAt.Kind.ShouldBe(DateTimeKind.Utc);
            build.QueuedAt.ShouldBe(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc));

            // Local is converted to the same instant in UTC.
            build.StartedAt.ShouldNotBeNull();
            build.StartedAt.Value.Kind.ShouldBe(DateTimeKind.Utc);
            build.StartedAt.Value.ShouldBe(local.ToUniversalTime());

            build.FinishedAt.ShouldBeNull();
            build.Duration.ShouldBeNull();
        }
    }

    [SkippableFact]
    public async Task Timestamps_KeepProviderPrecision()
    {
        var (_, project) = await CreateProjectAsync();

        // 1234567 ticks = 0.1234567 s: the last digit is below PostgreSQL's 1 µs resolution.
        var precise = new DateTime(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc).AddTicks(1_234_567);
        var duration = TimeSpan.FromTicks(98_765_432);

        var buildId = await CreateBuildAsync(project.Id, b =>
        {
            b.QueuedAt = precise;
            b.Duration = duration;
        });

        await using var scope = _services.CreateAsyncScope();
        var build = await Db(scope).Builds.AsNoTracking().SingleAsync(b => b.Id == buildId);

        if (_provider == DatabaseProvider.PostgreSql)
        {
            // timestamptz and interval store microseconds.
            Math.Abs((build.QueuedAt - precise).Ticks).ShouldBeLessThan(TimeSpan.TicksPerMicrosecond);
            (build.QueuedAt.Ticks % TimeSpan.TicksPerMicrosecond).ShouldBe(0);
            Math.Abs((build.Duration!.Value - duration).Ticks).ShouldBeLessThan(TimeSpan.TicksPerMicrosecond);
        }
        else
        {
            // datetime2(7) and time(7) keep 100 ns ticks.
            build.QueuedAt.ShouldBe(precise);
            build.Duration.ShouldBe(duration);
        }
    }

    // -------------------------------------------------------------------------
    // Identity and seeding
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task Seeding_CreatesRoles_AndIsIdempotent()
    {
        await SeedRolesAsync();
        await SeedRolesAsync();

        await using var scope = _services.CreateAsyncScope();
        var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
        foreach (var role in UserRoles.AllRoles)
        {
            (await roleManager.RoleExistsAsync(role)).ShouldBeTrue();
        }

        (await Db(scope).Roles.CountAsync()).ShouldBe(UserRoles.AllRoles.Length);
    }

    [SkippableFact]
    public async Task Identity_LookupByNormalizedEmail_RolesAndLockout_RoundTrip()
    {
        await SeedRolesAsync();
        var lockoutEnd = new DateTimeOffset(2030, 1, 2, 3, 4, 5, TimeSpan.Zero).AddTicks(1_234_560);

        await using (var scope = _services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var user = NewUser("Mixed.Case@Example.com");
            (await userManager.CreateAsync(user, "Passw0rd!x")).Succeeded.ShouldBeTrue();
            (await userManager.AddToRoleAsync(user, UserRoles.Admin)).Succeeded.ShouldBeTrue();
            (await userManager.SetLockoutEnabledAsync(user, true)).Succeeded.ShouldBeTrue();
            (await userManager.SetLockoutEndDateAsync(user, lockoutEnd)).Succeeded.ShouldBeTrue();

            // A second user without GitHubId: the unique GitHubId index must allow many NULLs.
            (await userManager.CreateAsync(NewUser("other@example.com"), "Passw0rd!x")).Succeeded.ShouldBeTrue();
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

            var found = await userManager.FindByEmailAsync("MIXED.CASE@example.COM");
            found.ShouldNotBeNull();
            found.Email.ShouldBe("Mixed.Case@Example.com");
            (await userManager.FindByNameAsync("mixed.case@example.com")).ShouldNotBeNull();

            (await userManager.IsInRoleAsync(found, UserRoles.Admin)).ShouldBeTrue();
            (await userManager.IsInRoleAsync(found, UserRoles.User)).ShouldBeFalse();
            (await userManager.GetUsersInRoleAsync(UserRoles.Admin)).Select(u => u.Id).ShouldBe([found.Id]);

            (await userManager.IsLockedOutAsync(found)).ShouldBeTrue();
            var storedEnd = await userManager.GetLockoutEndDateAsync(found);
            storedEnd.ShouldNotBeNull();
            storedEnd.Value.Offset.ShouldBe(TimeSpan.Zero);
            storedEnd.Value.UtcDateTime.ShouldBe(lockoutEnd.UtcDateTime);
        }
    }

    // -------------------------------------------------------------------------
    // Defaults
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task SystemSettings_ExplicitFalse_IsPersisted()
    {
        await using (var scope = _services.CreateAsyncScope())
        {
            var db = Db(scope);
            db.SystemSettings.Add(new SystemSettings { Id = 1, AllowUserRegistration = false });
            await db.SaveChangesAsync();
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var settings = await Db(scope).SystemSettings.AsNoTracking().SingleAsync();
            settings.AllowUserRegistration.ShouldBeFalse();
        }
    }

    [SkippableFact]
    public async Task Project_ColumnDefaults_ApplyWhenOmitted()
    {
        var (owner, _) = await CreateProjectAsync();
        var repoId = Random.Shared.NextInt64(1_000_000, long.MaxValue);
        var createdAt = DateTime.UtcNow;

        // Insert without TimeoutMinutes/DefaultBranch/BranchFilter so the
        // migrated column defaults apply. Double-quoted identifiers work on both
        // providers (SqlClient enables QUOTED_IDENTIFIER).
        await using (var scope = _services.CreateAsyncScope())
        {
            await Db(scope).Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Projects" ("OwnerId", "GitHubRepoId", "RepoFullName", "RepoUrl", "EnablePrBuilds", "NotifyOnFailure", "CreatedAt")
                VALUES ({owner.Id}, {repoId}, {"o/defaults"}, {"https://github.com/o/defaults"}, {false}, {true}, {createdAt})
                """);
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var project = await Db(scope).Projects.AsNoTracking().SingleAsync(p => p.GitHubRepoId == repoId);
            project.TimeoutMinutes.ShouldBe(15);
            project.DefaultBranch.ShouldBe("main");
            project.BranchFilter.ShouldBe("main,master");
            project.EnablePrBuilds.ShouldBeFalse();
            project.LastBuildAt.ShouldBeNull();
        }
    }

    // -------------------------------------------------------------------------
    // Log sanitization
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task ServerBuildLogger_StripsNulBeforeStoring()
    {
        var (_, project) = await CreateProjectAsync();
        var buildId = await CreateBuildAsync(project.Id);

        await using (var scope = _services.CreateAsyncScope())
        {
            var hubContext = new Mock<IHubContext<BuildLogHub>> { DefaultValue = DefaultValue.Mock };
            var logger = new ServerBuildLogger(Db(scope), buildId, hubContext.Object, CancellationToken.None);

            logger.StepStarted("Comp\0ile");
            logger.Info("bin\0ary\0 output");
        }

        await using (var scope = _services.CreateAsyncScope())
        {
            var entries = await Db(scope).BuildLogEntries.AsNoTracking()
                .Where(e => e.BuildId == buildId)
                .OrderBy(e => e.Sequence)
                .ToListAsync();

            entries.Count.ShouldBe(2);
            entries[0].StepName.ShouldBe("Compile");
            entries[0].Message.ShouldBe("Compile");
            entries[1].Message.ShouldBe("binary output");
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static AndoDbContext Db(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<AndoDbContext>();

    private static ApplicationUser NewUser(string email) => new()
    {
        UserName = email,
        Email = email,
        CreatedAt = DateTime.UtcNow
    };

    private static Build NewBuild(int projectId) => new()
    {
        ProjectId = projectId,
        CommitSha = Guid.NewGuid().ToString("N"),
        Branch = "main",
        Trigger = BuildTrigger.Push,
        QueuedAt = DateTime.UtcNow
    };

    private async Task SeedRolesAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        await DatabaseSeeding.SeedRolesAsync(scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>());
    }

    private async Task<(ApplicationUser Owner, Project Project)> CreateProjectAsync()
    {
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        var owner = NewUser($"owner_{Guid.NewGuid():N}@example.com");
        var project = new Project
        {
            Owner = owner,
            GitHubRepoId = Random.Shared.NextInt64(1, 1_000_000),
            RepoFullName = "owner/repo",
            RepoUrl = "https://github.com/owner/repo",
            CreatedAt = DateTime.UtcNow
        };
        db.Projects.Add(project);
        await db.SaveChangesAsync();
        return (owner, project);
    }

    private async Task<int> CreateBuildAsync(int projectId, Action<Build>? configure = null)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        var build = NewBuild(projectId);
        configure?.Invoke(build);
        db.Builds.Add(build);
        await db.SaveChangesAsync();
        return build.Id;
    }

    // Each insert uses its own scope so a failed SaveChanges leaves no tracked
    // entity behind for later steps.
    private async Task AddSecretAsync(int projectId, string name)
    {
        await using var scope = _services.CreateAsyncScope();
        var db = Db(scope);
        db.ProjectSecrets.Add(new ProjectSecret
        {
            ProjectId = projectId,
            Name = name,
            NormalizedName = ProjectSecret.Normalize(name),
            EncryptedValue = "encrypted",
            CreatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }
}
