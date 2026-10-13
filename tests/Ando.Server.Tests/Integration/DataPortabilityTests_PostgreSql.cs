// =============================================================================
// DataPortabilityTests_PostgreSql.cs
//
// Summary: Runs the data export/import tests with source and target databases
// on PostgreSql.
//
// Design Decisions:
// - Test bodies live in DataPortabilityTestsBase; this class only binds them
//   to the PostgreSql container collection
// =============================================================================

using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Data export/import tests against PostgreSql.
/// </summary>
[Trait("Category", "Integration")]
[Collection(PostgreSqlCollection.Name)]
public class DataPortabilityTests_PostgreSql : DataPortabilityTestsBase
{
    /// <summary>
    /// Creates the tests for the shared PostgreSql container.
    /// </summary>
    /// <param name="fixture">PostgreSql container fixture.</param>
    public DataPortabilityTests_PostgreSql(PostgreSqlFixture fixture) : base(fixture)
    {
    }
}
