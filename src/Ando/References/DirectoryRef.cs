// =============================================================================
// DirectoryRef.cs
//
// Summary: Represents a reference to a directory in build scripts.
//
// DirectoryRef provides type-safe references to directories for use with
// operations like Npm and Cloudflare that need a working directory context.
// It automatically extracts the directory name for use in logging.
//
// Example usage:
//   var frontend = Directory("./frontend");
//   Npm.Ci(frontend);
//   Npm.Run(frontend, "build");
//   Cloudflare.PagesDeploy(frontend, o => o.WithProjectName("my-site"));
//
// Design Decisions:
// - Simple constructor since directories are just paths (no validation needed)
// - Implicit string conversion allows seamless use with path-based APIs
// - Name derived from directory name for concise logging
// =============================================================================

namespace Ando.References;

/// <summary>
/// Represents a reference to a directory.
/// Provides type-safe directory references for operations that need a working directory.
/// </summary>
public class DirectoryRef
{
    /// <summary>
    /// The path to the directory.
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The directory name (derived from the path).
    /// Used in logging and step names for clarity.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates a directory reference from a path.
    /// </summary>
    /// <param name="path">Path to the directory.</param>
    public DirectoryRef(string path)
    {
        Path = path;
        // Get the last component of the path as the name.
        Name = System.IO.Path.GetFileName(path.TrimEnd('/', '\\')) ?? path;
    }

    public override string ToString() => Name;

    // Implicit string conversion allows DirectoryRef to be used where string paths are expected.
    public static implicit operator string(DirectoryRef directory) => directory.Path;

    /// <summary>
    /// Combines this directory with a subdirectory path.
    /// Allows syntax like: website / "dist"
    /// </summary>
    public static DirectoryRef operator /(DirectoryRef directory, string subPath)
    {
        var combinedPath = System.IO.Path.Combine(directory.Path, subPath);
        return new DirectoryRef(combinedPath);
    }
}
