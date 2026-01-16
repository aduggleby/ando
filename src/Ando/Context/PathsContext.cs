// =============================================================================
// PathsContext.cs
//
// Summary: Provides core path references for ANDO builds.
//
// PathsContext provides the Root and Temp paths used by build scripts.
// These are exposed as top-level globals (Root, Temp) in build.csando scripts.
//
// Design Decisions:
// - Root is the project directory where build.csando is located
// - Temp uses .ando/tmp to isolate temporary files from project files
// - Only essential paths are provided; projects define their own structure
// =============================================================================

namespace Ando.Context;

/// <summary>
/// Provides core path references for ANDO builds.
/// </summary>
public class PathsContext
{
    /// <summary>Project root directory (where build.csando is located).</summary>
    public BuildPath Root { get; }

    /// <summary>Temporary files directory (root/.ando/tmp).</summary>
    public BuildPath Temp { get; }

    /// <summary>
    /// Initializes path context from the project root directory.
    /// </summary>
    /// <param name="rootPath">Path to the project root (where build.csando is located).</param>
    public PathsContext(string rootPath)
    {
        Root = new BuildPath(rootPath);
        Temp = Root / ".ando" / "tmp";
    }

    /// <summary>
    /// Creates the temp directory if it doesn't exist.
    /// Called before build execution to ensure the location is available.
    /// </summary>
    public void EnsureDirectoriesExist()
    {
        Directory.CreateDirectory(Temp.Value);
    }
}
