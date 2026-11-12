// =============================================================================
// GetSystemHealthEndpointTests.cs
//
// Summary: Unit tests for the admin system health endpoint's response shape.
//
// Verifies that database connectivity and Hangfire job storage are reported
// as separate checks with stable keys, and that the database check names the
// configured provider.
//
// Design Decisions:
// - Uses FastEndpoints' Factory.Create with an EF InMemory context, so no
//   HTTP host or admin login is needed
// - Does not assert the job storage status: it depends on global Hangfire
//   state (JobStorage.Current) shared with other tests in the process
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Endpoints.Admin;
using Ando.Server.Tests.TestFixtures;
using FastEndpoints;
using Microsoft.Extensions.Options;

namespace Ando.Server.Tests.Unit.Endpoints;

/// <summary>
/// Tests for <see cref="GetSystemHealthEndpoint"/>.
/// </summary>
[Collection(Ando.Server.Tests.TestFixtures.FastEndpointsFactoryCollection.Name)]
public class GetSystemHealthEndpointTests
{
    public GetSystemHealthEndpointTests() => Ando.Server.Tests.TestFixtures.FastEndpointsFactoryCollection.ResetResolver();

    [Theory]
    [InlineData(DatabaseProvider.SqlServer)]
    [InlineData(DatabaseProvider.PostgreSql)]
    public async Task HandleAsync_ReportsDatabaseAndJobStorageSeparately_WithProvider(DatabaseProvider provider)
    {
        using var db = TestDbContextFactory.Create();
        var endpoint = Factory.Create<GetSystemHealthEndpoint>(
            db,
            new MockGitHubService(),
            Options.Create(new DatabaseSettings { Provider = provider }));

        await endpoint.HandleAsync(CancellationToken.None);

        var checks = endpoint.Response.Checks;
        checks.Select(c => c.Key).ShouldBe(
        [
            GetSystemHealthEndpoint.DatabaseKey,
            GetSystemHealthEndpoint.JobStorageKey,
            GetSystemHealthEndpoint.GitHubKey
        ]);

        var database = checks.Single(c => c.Key == GetSystemHealthEndpoint.DatabaseKey);
        database.Name.ShouldBe("Database");
        database.Status.ShouldBe("healthy");
        database.Provider.ShouldBe(provider.ToString());
        database.Message.ShouldStartWith(provider.ToString());

        var jobStorage = checks.Single(c => c.Key == GetSystemHealthEndpoint.JobStorageKey);
        jobStorage.Name.ShouldBe("Background Jobs");
        jobStorage.Provider.ShouldBeNull();
    }
}
