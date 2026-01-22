// =============================================================================
// ProjectRef.cs
//
// Summary: Represents a reference to a project file in build scripts.
//
// ProjectRef provides type-safe references to project files (.csproj, package.json)
// for use with build operations. It automatically extracts the project name and
// directory from the path for convenient use in logging and step registration.
//
// Example usage:
//   var project = Project.From("./src/MyApp/MyApp.csproj");
//   Dotnet.Build(project);  // Uses project.Path for the dotnet command
//   // Logs show project.Name ("MyApp") for clarity
//   Log.Info($"Building version: {project.Version}");
//
// Design Decisions:
// - Factory method From() rather than public constructor for discoverability
// - Private constructor ensures all instances go through validation
// - Implicit string conversion allows seamless use with path-based APIs
// - Name derived from file name (without extension) for concise logging
// - Version is lazily loaded and cached from the .csproj file
// =============================================================================

using System.Text.RegularExpressions;
using Ando.Context;

namespace Ando.References;

/// <summary>
/// Represents a reference to a project file (e.g., .csproj, package.json).
/// Provides type-safe project references for build operations.
/// </summary>
public class ProjectRef
{
    /// <summary>
    /// The path to the project file (e.g., .csproj, package.json).
    /// </summary>
    public string Path { get; }

    /// <summary>
    /// The project name (derived from file name without extension).
    /// Used in logging and step names for clarity.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The directory containing the project file.
    /// Useful for npm operations that need the package.json directory.
    /// </summary>
    public string Directory { get; }

    // Cached version value.
    private string? _version;
    private bool _versionLoaded;

    /// <summary>
    /// The project version (read from the Version element in .csproj).
    /// Returns "0.0.0" if no Version element is found.
    /// The version is lazily loaded and cached.
    /// </summary>
    public string Version
    {
        get
        {
            if (!_versionLoaded)
            {
                _version = ReadVersionFromProject();
                _versionLoaded = true;
            }
            return _version ?? "0.0.0";
        }
    }

    // Private constructor - use From() factory method.
    private ProjectRef(string path)
    {
        Path = path;
        Name = System.IO.Path.GetFileNameWithoutExtension(path);
        Directory = System.IO.Path.GetDirectoryName(path) ?? ".";
    }

    /// <summary>
    /// Creates a project reference from a relative or absolute path.
    /// </summary>
    /// <param name="relativePath">Path to the project file.</param>
    public static ProjectRef From(string relativePath)
    {
        return new ProjectRef(relativePath);
    }

    /// <summary>
    /// Converts to BuildPath for path composition.
    /// </summary>
    public BuildPath ToBuildPath() => new BuildPath(Path);

    public override string ToString() => Name;

    // Implicit string conversion allows ProjectRef to be used where string paths are expected.
    public static implicit operator string(ProjectRef project) => project.Path;

    // Reads the Version element from the project file.
    // Returns null if the file doesn't exist, isn't a .csproj, or has no Version element.
    private string? ReadVersionFromProject()
    {
        // Only .csproj files have Version elements.
        if (!Path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // Resolve the path relative to current working directory.
        var fullPath = System.IO.Path.IsPathRooted(Path)
            ? Path
            : System.IO.Path.Combine(Environment.CurrentDirectory, Path);

        if (!File.Exists(fullPath))
        {
            return null;
        }

        try
        {
            var content = File.ReadAllText(fullPath);

            // Match <Version>x.y.z</Version> pattern.
            // Supports versions with optional prerelease suffix (e.g., 1.0.0-preview).
            var match = Regex.Match(content, @"<Version>([^<]+)</Version>");
            if (match.Success)
            {
                return match.Groups[1].Value.Trim();
            }

            return null;
        }
        catch
        {
            // If we can't read the file for any reason, return null.
            return null;
        }
    }
}
