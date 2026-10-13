// =============================================================================
// DataPortabilityTests_SqlServer.cs
//
// Summary: Runs the data export/import tests with source and target databases
// on SqlServer.
//
// Design Decisions:
// - Test bodies live in DataPortabilityTestsBase; this class only binds them
//   to the SqlServer container collection
// =============================================================================

using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Data export/import tests against SqlServer.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerCollection.Name)]
public class DataPortabilityTests_SqlServer : DataPortabilityTestsBase
{
    /// <summary>
    /// Creates the tests for the shared SqlServer container.
    /// </summary>
    /// <param name="fixture">SqlServer container fixture.</param>
    public DataPortabilityTests_SqlServer(SqlServerFixture fixture) : base(fixture)
    {
    }
}
