// =============================================================================
// VersionWriter.cs
//
// Summary: Writes version information to project files.
//
// This class updates version strings in .csproj and package.json files.
// It preserves the original file formatting as much as possible using
// regex replacement rather than re-serializing the entire file.
//
// Design Decisions:
// - Uses regex replacement to preserve formatting and whitespace
// - Validates version before writing to catch errors early
// - Does not create Version element if it doesn't exist (throws instead)
// =============================================================================

using System.Text.RegularExpressions;
using static Ando.Versioning.ProjectDetector;

namespace Ando.Versioning;

/// <summary>
/// Writes version information to project files (.csproj, package.json).
/// </summary>
public partial class VersionWriter
{
    // Regex for .csproj Version element.
    [GeneratedRegex(@"<Version>[^<]*</Version>", RegexOptions.Compiled)]
    private static partial Regex CsprojVersionRegex();

    // Regex for package.json version field.
    [GeneratedRegex(@"""version""\s*:\s*""[^""]*""", RegexOptions.Compiled)]
    private static partial Regex PackageJsonVersionRegex();

    /// <summary>
    /// Writes a new version to a project file.
    /// </summary>
    /// <param name="projectPath">Path to the project file.</param>
    /// <param name="type">Type of project.</param>
    /// <param name="newVersion">New version string to write.</param>
    /// <exception cref="ArgumentException">Thrown if version format is invalid.</exception>
    /// <exception cref="InvalidOperationException">Thrown if version element not found.</exception>
    public void WriteVersion(string projectPath, ProjectType type, string newVersion)
    {
        // Validate the version format.
        if (!SemVer.TryParse(newVersion, out _))
        {
            throw new ArgumentException($"Invalid version format: {newVersion}");
        }

        switch (type)
        {
            case ProjectType.Dotnet:
                WriteCsprojVersion(projectPath, newVersion);
                break;
            case ProjectType.Npm:
                WritePackageJsonVersion(projectPath, newVersion);
                break;
        }
    }

    /// <summary>
    /// Writes version to a .csproj file by replacing the Version element.
    /// </summary>
    private static void WriteCsprojVersion(string path, string version)
    {
        var content = File.ReadAllText(path);

        if (!CsprojVersionRegex().IsMatch(content))
        {
            throw new InvalidOperationException(
                $"No <Version> element found in {path}. Add a Version element to the csproj first.");
        }

        var updated = CsprojVersionRegex().Replace(content, $"<Version>{version}</Version>");
        File.WriteAllText(path, updated);
    }

    /// <summary>
    /// Writes version to a package.json file by replacing the version field.
    /// </summary>
    private static void WritePackageJsonVersion(string path, string version)
    {
        var content = File.ReadAllText(path);

        if (!PackageJsonVersionRegex().IsMatch(content))
        {
            throw new InvalidOperationException(
                $"No \"version\" field found in {path}.");
        }

        var updated = PackageJsonVersionRegex().Replace(content, $@"""version"": ""{version}""");
        File.WriteAllText(path, updated);
    }
}
