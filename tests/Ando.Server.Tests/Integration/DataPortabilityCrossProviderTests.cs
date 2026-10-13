// =============================================================================
// DataPortabilityCrossProviderTests.cs
//
// Summary: The migration this feature exists for: export from one provider,
// import into the other, and assert the data is identical (apart from the
// documented Queued/Running -> Cancelled rule).
//
// Design Decisions:
// - Uses CrossProviderDatabaseFixture (private isolated containers for both
//   providers) because a test class cannot join both provider collections
// - Skipped unless both providers are selected by ANDO_TEST_DB_PROVIDERS
// =============================================================================

using Ando.Server.Configuration;
using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// SQL Server to PostgreSQL and PostgreSQL to SQL Server round trips.
/// </summary>
[Trait("Category", "Integration")]
[Collection(CrossProviderCollection.Name)]
public class DataPortabilityCrossProviderTests
{
    private readonly CrossProviderDatabaseFixture _fixture;

    /// <summary>
    /// Creates the tests, or skips them when a provider is deselected.
    /// </summary>
    /// <param name="fixture">Fixture with both containers.</param>
    public DataPortabilityCrossProviderTests(CrossProviderDatabaseFixture fixture)
    {
        Skip.IfNot(CrossProviderDatabaseFixture.BothSelected, CrossProviderDatabaseFixture.SkipReason);
        _fixture = fixture;
    }

    [SkippableFact]
    public Task RoundTrip_SqlServerToPostgreSql_PreservesAllData() =>
        DataPortabilityTestData.AssertRoundTripAsync(
            _fixture.For(DatabaseProvider.SqlServer), _fixture.For(DatabaseProvider.PostgreSql));

    [SkippableFact]
    public Task RoundTrip_PostgreSqlToSqlServer_PreservesAllData() =>
        DataPortabilityTestData.AssertRoundTripAsync(
            _fixture.For(DatabaseProvider.PostgreSql), _fixture.For(DatabaseProvider.SqlServer));
}
