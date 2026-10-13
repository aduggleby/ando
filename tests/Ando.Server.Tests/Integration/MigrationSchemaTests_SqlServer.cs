// =============================================================================
// MigrationSchemaTests_SqlServer.cs
//
// Summary: Runs the migration and behavioral schema tests against SQL Server
// using the Migrations/SqlServer set.
//
// Design Decisions:
// - Test bodies live in MigrationSchemaTestsBase; this class only binds them
//   to the SqlServer container collection
// =============================================================================

using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Migration and behavioral schema tests against SQL Server.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerCollection.Name)]
public class MigrationSchemaTests_SqlServer : MigrationSchemaTestsBase
{
    /// <summary>
    /// Creates the tests for the shared SQL Server container.
    /// </summary>
    /// <param name="fixture">SQL Server container fixture.</param>
    public MigrationSchemaTests_SqlServer(SqlServerFixture fixture) : base(fixture)
    {
    }
}
