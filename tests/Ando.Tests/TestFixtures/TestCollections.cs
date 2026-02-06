// =============================================================================
// TestCollections.cs
//
// Summary: XUnit test collection definitions for test isolation.
//
// Tests that modify process-wide state (like Directory.SetCurrentDirectory)
// must run sequentially to avoid interfering with each other. This file defines
// collections that ensure such tests don't run in parallel.
// =============================================================================

namespace Ando.Tests.TestFixtures;

/// <summary>
/// Collection for tests that change the process working directory.
/// Tests in this collection run sequentially to avoid race conditions.
/// </summary>
[CollectionDefinition("DirectoryChangingTests", DisableParallelization = true)]
public class DirectoryChangingTestsCollection
{
}
