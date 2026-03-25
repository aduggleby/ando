// =============================================================================
// EnvironmentVariableCollection.cs
//
// Summary: Serializes tests that mutate process-wide environment variables.
//
// Environment variables are shared across the current test process, so tests
// that modify them cannot safely run in parallel. This collection is applied
// to tests that exercise env loading and forwarding behavior.
// =============================================================================

namespace Ando.Tests.Unit;

/// <summary>
/// Non-parallel collection for tests that modify environment variables.
/// </summary>
[CollectionDefinition("Environment Variables", DisableParallelization = true)]
public class EnvironmentVariableCollection
{
}
