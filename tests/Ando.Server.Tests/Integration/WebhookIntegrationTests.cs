// =============================================================================
// WebhookIntegrationTests.cs
//
// Summary: Binds the provider-agnostic webhook integration tests to each
// database provider's container collection.
//
// Design Decisions:
// - One concrete class per provider so test names show the provider and each
//   class joins the collection that owns that provider's container
// - Test bodies live in WebhookIntegrationTestsBase and are identical for
//   both providers
// =============================================================================

using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Webhook integration tests against SQL Server.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerCollection.Name)]
public class WebhookIntegrationTests_SqlServer : WebhookIntegrationTestsBase
{
    /// <summary>
    /// Creates the tests for the shared SQL Server container.
    /// </summary>
    /// <param name="fixture">SQL Server container fixture.</param>
    public WebhookIntegrationTests_SqlServer(SqlServerFixture fixture) : base(fixture)
    {
    }
}

/// <summary>
/// Webhook integration tests against PostgreSQL.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public class WebhookIntegrationTests_PostgreSql : WebhookIntegrationTestsBase
{
    /// <summary>
    /// Creates the tests for the shared PostgreSQL container.
    /// </summary>
    /// <param name="fixture">PostgreSQL container fixture.</param>
    public WebhookIntegrationTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture)
    {
    }
}
