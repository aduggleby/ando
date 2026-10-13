// =============================================================================
// HangfireStorageTests_SqlServer.cs
//
// Summary: Runs the real-storage Hangfire worker tests on SQL Server storage
// (Hangfire.SqlServer with SlidingInvisibilityTimeout).
//
// Design Decisions:
// - Test bodies live in HangfireStorageTestsBase; this class only binds them
//   to the SqlServer container collection
// =============================================================================

using Ando.Server.Tests.TestFixtures;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Hangfire real-storage tests against SQL Server.
/// </summary>
[Trait("Category", "Integration")]
[Collection(SqlServerCollection.Name)]
public class HangfireStorageTests_SqlServer : HangfireStorageTestsBase
{
    /// <summary>
    /// Creates the tests for the shared SQL Server container.
    /// </summary>
    /// <param name="fixture">SQL Server container fixture.</param>
    public HangfireStorageTests_SqlServer(SqlServerFixture fixture) : base(fixture)
    {
    }
}
