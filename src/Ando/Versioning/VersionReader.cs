// =============================================================================
// VersionReader.cs
//
// Summary: Reads version information from project files.
//
// This class extracts version strings from .csproj and package.json files.
// It validates that the version is a valid semantic version.
//
// Design Decisions:
// - Uses XDocument for .csproj to properly handle XML namespaces
// - Uses JsonDocument for package.json to avoid external dependencies
// - Validates versions with SemVer.TryParse before returning
// - Returns null if no version is found (rather than throwing)
// =============================================================================

using System.Text.Json;
using System.Xml.Linq;
using static Ando.Versioning.ProjectDetector;

namespace Ando.Versioning;

/// <summary>
/// Reads version information from project files (.csproj, package.json).
/// </summary>
public class VersionReader
{
    /// <summary>
    /// Reads the version from a project file.
    /// </summary>
    /// <param name="projectPath">Path to the project file.</param>
    /// <param name="type">Type of project.</param>
    /// <returns>Version string, or null if not found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if version format is invalid.</exception>
    public string? ReadVersion(string projectPath, ProjectType type)
    {
        var version = type switch
        {
            ProjectType.Dotnet => ReadCsprojVersion(projectPath),
            ProjectType.Npm => ReadPackageJsonVersion(projectPath),
            _ => null
        };

        // Validate the version if found.
        if (version != null && !SemVer.TryParse(version, out _))
        {
            throw new InvalidOperationException(
                $"Invalid version format '{version}' in {projectPath}");
        }

        return version;
    }

    /// <summary>
    /// Reads version from a .csproj file.
    /// Looks for &lt;Version&gt; element in PropertyGroup.
    /// </summary>
    private static string? ReadCsprojVersion(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var doc = XDocument.Load(path);

            // Look for Version element in any PropertyGroup.
            // This handles both conditional and non-conditional PropertyGroups.
            var versionElement = doc.Descendants()
                .FirstOrDefault(e => e.Name.LocalName == "Version");

            return versionElement?.Value;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Reads version from a package.json file.
    /// </summary>
    private static string? ReadPackageJsonVersion(string path)
    {
        if (!File.Exists(path))
            return null;

        try
        {
            var content = File.ReadAllText(path);
            var doc = JsonDocument.Parse(content);

            if (doc.RootElement.TryGetProperty("version", out var versionProp))
            {
                return versionProp.GetString();
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}
