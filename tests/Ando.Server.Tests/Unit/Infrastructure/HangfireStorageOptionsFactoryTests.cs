// =============================================================================
// HangfireStorageOptionsFactoryTests.cs
//
// Summary: Guards the production Hangfire storage options that prevent a long
// build from being executed twice.
//
// The real-storage Hangfire tests use the same factory with shorter timings;
// these tests pin the production values themselves, so turning off lease
// renewal or changing the schema fails without Docker.
// =============================================================================

using Ando.Server.Infrastructure.Database;

namespace Ando.Server.Tests.Unit.Infrastructure;

/// <summary>
/// Tests for <see cref="HangfireStorageOptionsFactory"/>.
/// </summary>
public class HangfireStorageOptionsFactoryTests
{
    [Fact]
    public void PostgreSqlOptions_Production_UseSlidingInvisibilityTimeoutAndHangfireSchema()
    {
        var options = HangfireStorageOptionsFactory.CreatePostgreSqlOptions();

        options.UseSlidingInvisibilityTimeout.ShouldBeTrue();
        options.SchemaName.ShouldBe("hangfire");
        options.InvisibilityTimeout.ShouldBe(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public void SqlServerOptions_Production_KeepSlidingInvisibilityTimeout()
    {
        var options = HangfireStorageOptionsFactory.CreateSqlServerOptions();

        options.SlidingInvisibilityTimeout.ShouldBe(TimeSpan.FromMinutes(5));
        options.QueuePollInterval.ShouldBe(TimeSpan.Zero);
        options.DisableGlobalLocks.ShouldBeTrue();
    }

    [Fact]
    public void Overrides_ChangeOnlyTimings()
    {
        var timeout = TimeSpan.FromSeconds(2);
        var poll = TimeSpan.FromMilliseconds(200);

        var postgres = HangfireStorageOptionsFactory.CreatePostgreSqlOptions(timeout, poll);
        postgres.InvisibilityTimeout.ShouldBe(timeout);
        postgres.QueuePollInterval.ShouldBe(poll);
        postgres.UseSlidingInvisibilityTimeout.ShouldBeTrue();
        postgres.SchemaName.ShouldBe("hangfire");

        var sqlServer = HangfireStorageOptionsFactory.CreateSqlServerOptions(timeout, poll);
        sqlServer.SlidingInvisibilityTimeout.ShouldBe(timeout);
        sqlServer.QueuePollInterval.ShouldBe(poll);
        sqlServer.DisableGlobalLocks.ShouldBeTrue();
    }
}
