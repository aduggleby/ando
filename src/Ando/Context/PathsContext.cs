// =============================================================================
// PathsContext.cs
//
// Summary: Provides standardized path conventions for ANDO builds.
//
// PathsContext establishes a consistent directory layout across all ANDO projects,
// making build scripts more portable and predictable. It defines standard locations
// for source code, build outputs, and temporary files.
//
// Design Decisions:
// - Standardized directory layout reduces per-project configuration
// - Uses .ando subdirectory for build-system files to avoid cluttering root
// - Artifacts directory is at root level for easy access to build outputs
// - Temp files isolated in .ando/tmp to simplify cleanup
// =============================================================================

namespace Ando.Context;

/// <summary>
/// Provides standardized directory paths for ANDO builds.
/// Establishes consistent conventions for source, output, and temp directories.
/// </summary>
public class PathsContext
{
    /// <summary>Project root directory (where build.ando is located).</summary>
    public BuildPath Root { get; }

    /// <summary>Standard source code directory (root/src).</summary>
    public BuildPath Src { get; }

    /// <summary>Build output directory (root/artifacts).</summary>
    public BuildPath Artifacts { get; }

    /// <summary>Temporary files directory (root/.ando/tmp).</summary>
    public BuildPath Temp { get; }

    /// <summary>
    /// Initializes path context from the project root directory.
    /// </summary>
    /// <param name="rootPath">Path to the project root (where build.ando is located).</param>
    public PathsContext(string rootPath)
    {
        Root = new BuildPath(rootPath);
        Src = Root / "src";
        Artifacts = Root / "artifacts";
        Temp = Root / ".ando" / "tmp";
    }

    /// <summary>
    /// Creates the artifacts and temp directories if they don't exist.
    /// Called before build execution to ensure output locations are available.
    /// </summary>
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Artifacts.Value);
        Directory.CreateDirectory(Temp.Value);
    }
}
