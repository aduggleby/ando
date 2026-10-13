// =============================================================================
// MigrationSchemaTests_PostgreSql.cs
//
// Summary: Runs the migration and behavioral schema tests against PostgreSQL
// using the Migrations/PostgreSql set.
//
// Design Decisions:
// - Test bodies live in MigrationSchemaTestsBase; this class only binds them
//   to the PostgreSql container collection
// =============================================================================

using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Migration and behavioral schema tests against PostgreSQL.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public class MigrationSchemaTests_PostgreSql : MigrationSchemaTestsBase
{
    /// <summary>
    /// Creates the tests for the shared PostgreSQL container.
    /// </summary>
    /// <param name="fixture">PostgreSQL container fixture.</param>
    public MigrationSchemaTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture)
    {
    }
}
