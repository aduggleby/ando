// =============================================================================
// HangfireStorageTestsBase.cs
//
// Summary: Runs real Hangfire servers on each provider's real job storage to
// prove that long builds are not executed twice and that queued work survives
// a server going away.
//
// The application never exercises real Hangfire storage in tests: Testing uses
// in-memory storage and E2E replaces Hangfire with no-op services. Lease bugs
// (a job becoming visible again while it is still running and being picked up
// by a second worker) only show up against the real storage, so these tests
// start real BackgroundJobServer instances on SQL Server / PostgreSQL storage.
//
// Design Decisions:
// - Storage options come from the production HangfireStorageOptionsFactory;
//   only the timings are overridden (2 s invisibility/sliding timeout, 200 ms
//   poll interval) so a 7 s job spans more than three renewal intervals in a
//   short test. Disabling lease renewal in production breaks these tests.
// - Waits throw TimeoutException when their condition is not reached, so a
//   missing prerequisite (e.g. the job never started) fails the test instead
//   of letting a later assertion pass for the wrong reason
// - JobStorage.Current is never set; servers and clients get the storage
//   explicitly, so tests cannot leak global Hangfire state
// - Each test gets its own database and its own queue name; executions are
//   counted in a static dictionary keyed by a per-test run id
// - The restart test asserts the first server started the job (started == 1)
//   before stopping it; the shutdown token aborts that execution, the job
//   goes back to the queue, and a NEW server starts it a second time
//   (started == 2) and completes it exactly once (completed == 1)
// =============================================================================

using System.Collections.Concurrent;
using Ando.Server.Configuration;
using Ando.Server.Infrastructure.Database;
using Ando.Server.Tests.TestFixtures;
using Hangfire;
using Hangfire.Common;
using Hangfire.PostgreSql;
using Hangfire.PostgreSql.Factories;
using Hangfire.SqlServer;
using Hangfire.States;
using Hangfire.Storage;
using Microsoft.Data.SqlClient;
using Npgsql;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Real-storage Hangfire worker tests shared by all database providers.
/// </summary>
[Trait("Category", "Integration")]
public abstract class HangfireStorageTestsBase : IAsyncLifetime
{
    /// <summary>
    /// Invisibility (PostgreSQL) / sliding invisibility (SQL Server) timeout used by the tests.
    /// </summary>
    protected static readonly TimeSpan InvisibilityTimeout = TimeSpan.FromSeconds(2);

    private const int LongJobMilliseconds = 7_000;

    private readonly IDatabaseFixture _fixture;
    private readonly string _databaseName = $"ando_hangfire_{Guid.NewGuid():N}";
    private readonly string _queue = $"it_{Guid.NewGuid():N}"[..16];
    private readonly string _runId = Guid.NewGuid().ToString("N");
    private readonly List<BackgroundJobServer> _servers = [];

    /// <summary>
    /// Captures the fixture, or skips the test when its provider is not selected.
    /// </summary>
    /// <param name="fixture">Container fixture for the provider under test.</param>
    protected HangfireStorageTestsBase(IDatabaseFixture fixture)
    {
        Skip.IfNot(DatabaseTestProviders.IsSelected(fixture.Provider), DatabaseTestProviders.SkipReason(fixture.Provider));
        _fixture = fixture;
    }

    /// <summary>
    /// The connection string to this test's database.
    /// </summary>
    protected string ConnectionString => _fixture.GetConnectionString(_databaseName);

    /// <summary>
    /// Creates the empty database Hangfire installs its schema into.
    /// </summary>
    public async Task InitializeAsync()
    {
        await ExecuteOnServerAsync(_fixture.Provider == DatabaseProvider.SqlServer
            ? $"CREATE DATABASE [{_databaseName}]"
            : $"CREATE DATABASE \"{_databaseName}\"");
    }

    /// <summary>
    /// Stops all servers started by the test and drops the database.
    /// </summary>
    public async Task DisposeAsync()
    {
        foreach (var server in _servers)
        {
            server.Dispose();
        }

        JobCounters.Forget(_runId);

        if (_fixture.Provider == DatabaseProvider.SqlServer)
        {
            SqlConnection.ClearAllPools();
            await ExecuteOnServerAsync(
                $"ALTER DATABASE [{_databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{_databaseName}]");
        }
        else
        {
            NpgsqlConnection.ClearAllPools();
            await ExecuteOnServerAsync($"DROP DATABASE IF EXISTS \"{_databaseName}\" WITH (FORCE)");
        }
    }

    // -------------------------------------------------------------------------
    // Tests
    // -------------------------------------------------------------------------

    [SkippableFact]
    public async Task LongJob_WithTwoServers_RunsExactlyOnce()
    {
        var storage = CreateStorage();
        StartServer(storage, "a");
        StartServer(storage, "b");

        var jobId = Enqueue(storage, () => JobCounters.SleepAndCount(_runId, LongJobMilliseconds, CancellationToken.None));

        await WaitForStateAsync(storage, jobId, SucceededState.StateName, TimeSpan.FromSeconds(20));

        // Give a late duplicate fetch time to show up before counting.
        await Task.Delay(InvisibilityTimeout * 2);
        JobCounters.Started(_runId).ShouldBe(1);
        JobCounters.Completed(_runId).ShouldBe(1);
    }

    [SkippableFact]
    public async Task RunningJob_WhenServerStops_IsCompletedByNewServer()
    {
        var storage = CreateStorage();
        var first = StartServer(storage, "first");

        var jobId = Enqueue(storage, () => JobCounters.SleepAndCount(_runId, LongJobMilliseconds, CancellationToken.None));

        await WaitUntilAsync(() => JobCounters.Started(_runId) >= 1, TimeSpan.FromSeconds(15),
            "the first server to start the job");
        await Task.Delay(TimeSpan.FromSeconds(1));

        // The first server is mid-run: started once, not completed.
        JobCounters.Started(_runId).ShouldBe(1);
        JobCounters.Completed(_runId).ShouldBe(0);

        // Simulated crash: stop the server while the job is mid-run. The job
        // observes the shutdown token and aborts without completing.
        first.Dispose();
        _servers.Remove(first);
        JobCounters.Started(_runId).ShouldBe(1);
        JobCounters.Completed(_runId).ShouldBe(0);

        StartServer(storage, "second");

        // Recovery: the new server starts the job again and completes it.
        await WaitUntilAsync(() => JobCounters.Started(_runId) >= 2, TimeSpan.FromSeconds(30),
            "the second server to start the job again");
        await WaitForStateAsync(storage, jobId, SucceededState.StateName, TimeSpan.FromSeconds(30));
        JobCounters.Started(_runId).ShouldBe(2);
        JobCounters.Completed(_runId).ShouldBe(1);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates job storage for the provider with test timeouts; installs the schema.
    /// </summary>
    /// <returns>The storage.</returns>
    protected JobStorage CreateStorage()
    {
        var pollInterval = TimeSpan.FromMilliseconds(200);

        if (_fixture.Provider == DatabaseProvider.SqlServer)
        {
            return new SqlServerStorage(
                ConnectionString,
                HangfireStorageOptionsFactory.CreateSqlServerOptions(InvisibilityTimeout, pollInterval));
        }

        var options = HangfireStorageOptionsFactory.CreatePostgreSqlOptions(InvisibilityTimeout, pollInterval);
        return new PostgreSqlStorage(new NpgsqlConnectionFactory(ConnectionString, options), options);
    }

    private BackgroundJobServer StartServer(JobStorage storage, string name)
    {
        var server = new BackgroundJobServer(
            new BackgroundJobServerOptions
            {
                ServerName = $"{_queue}-{name}",
                WorkerCount = 1,
                Queues = [_queue],
                ShutdownTimeout = TimeSpan.FromSeconds(5),
                SchedulePollingInterval = TimeSpan.FromSeconds(1)
            },
            storage);
        _servers.Add(server);
        return server;
    }

    private string Enqueue(JobStorage storage, System.Linq.Expressions.Expression<Action> call)
    {
        var client = new BackgroundJobClient(storage);
        return client.Create(Job.FromExpression(call), new EnqueuedState(_queue));
    }

    private static async Task WaitForStateAsync(JobStorage storage, string jobId, string state, TimeSpan timeout)
    {
        string? current = null;
        await WaitUntilAsync(() =>
        {
            using IStorageConnection connection = storage.GetConnection();
            current = connection.GetJobData(jobId)?.State;
            return current == state;
        }, timeout, () => $"job {jobId} to reach state {state} (last observed: {current ?? "<none>"})");
    }

    private static Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, string description) =>
        WaitUntilAsync(condition, timeout, () => description);

    // Polls until the condition holds; throws TimeoutException naming what was awaited otherwise.
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout, Func<string> description)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
            {
                throw new TimeoutException($"Timed out after {timeout.TotalSeconds:0.#} s waiting for {description()}.");
            }

            await Task.Delay(100);
        }
    }

    private async Task ExecuteOnServerAsync(string sql)
    {
        if (_fixture.Provider == DatabaseProvider.SqlServer)
        {
            await using var connection = new SqlConnection(_fixture.GetConnectionString("master"));
            await connection.OpenAsync();
            await using var command = new SqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
        else
        {
            await using var connection = new NpgsqlConnection(_fixture.GetConnectionString("postgres"));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            await command.ExecuteNonQueryAsync();
        }
    }
}

/// <summary>
/// Static job target for the Hangfire storage tests; counts executions per run id.
/// </summary>
public static class JobCounters
{
    private static readonly ConcurrentDictionary<string, int> StartedCounts = new();
    private static readonly ConcurrentDictionary<string, int> CompletedCounts = new();

    /// <summary>
    /// Counts a start, sleeps (observing the Hangfire shutdown token), then counts a completion.
    /// </summary>
    /// <param name="runId">Test run identifier.</param>
    /// <param name="milliseconds">How long the job runs.</param>
    /// <param name="cancellationToken">Replaced by Hangfire with the job's shutdown token.</param>
    public static void SleepAndCount(string runId, int milliseconds, CancellationToken cancellationToken)
    {
        StartedCounts.AddOrUpdate(runId, 1, (_, count) => count + 1);
        Task.Delay(milliseconds, cancellationToken).GetAwaiter().GetResult();
        CompletedCounts.AddOrUpdate(runId, 1, (_, count) => count + 1);
    }

    /// <summary>
    /// Number of times the job started for <paramref name="runId"/>.
    /// </summary>
    /// <param name="runId">Test run identifier.</param>
    /// <returns>The start count.</returns>
    public static int Started(string runId) => StartedCounts.GetValueOrDefault(runId);

    /// <summary>
    /// Number of times the job ran to completion for <paramref name="runId"/>.
    /// </summary>
    /// <param name="runId">Test run identifier.</param>
    /// <returns>The completion count.</returns>
    public static int Completed(string runId) => CompletedCounts.GetValueOrDefault(runId);

    /// <summary>
    /// Removes the counters for <paramref name="runId"/>.
    /// </summary>
    /// <param name="runId">Test run identifier.</param>
    public static void Forget(string runId)
    {
        StartedCounts.TryRemove(runId, out _);
        CompletedCounts.TryRemove(runId, out _);
    }
}
