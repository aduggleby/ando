// =============================================================================
// BuildContextObject.cs
//
// Summary: Unified context object exposed to build scripts as 'Context'.
//
// BuildContextObject aggregates context information available to build
// scripts into a single object. This provides access to paths and build state.
//
// Design Decisions:
// - Single entry point reduces cognitive load for script authors
// - Aggregates specialized contexts (Paths) rather than inheriting
// - Initialized once per build and immutable to prevent unexpected changes
// - Named "BuildContextObject" to avoid confusion with System.Threading context
// =============================================================================

namespace Ando.Context;

/// <summary>
/// Unified context object exposed to build scripts as 'Context'.
/// Provides access to paths and build state.
/// </summary>
public class BuildContextObject
{
    /// <summary>Provides access to standardized project paths.</summary>
    public PathsContext Paths { get; }

    /// <summary>
    /// Creates a new build context rooted at the specified directory.
    /// </summary>
    /// <param name="rootPath">The project root directory (where build.csando is located).</param>
    public BuildContextObject(string rootPath)
    {
        Paths = new PathsContext(rootPath);
    }

    /// <summary>
    /// Ensures all required directories exist.
    /// Called before build execution to prepare the output locations.
    /// </summary>
    public void EnsureDirectoriesExist()
    {
        Paths.EnsureDirectoriesExist();
    }
}
