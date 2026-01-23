// =============================================================================
// ProjectDetector.cs
//
// Summary: Parses build.csando to find project references.
//
// This class scans build script files to detect .NET projects (Dotnet.Project)
// and npm projects (Directory + Npm operations). Uses regex pattern matching
// for speed and simplicity rather than full Roslyn parsing.
//
// Design Decisions:
// - Regex pattern matching is sufficient for well-formed build scripts
// - Returns relative paths as specified in the build script
// - Validates that detected files exist before returning
// - Supports both variable assignments and inline usage
// =============================================================================

using System.Text.RegularExpressions;

namespace Ando.Versioning;

/// <summary>
/// Detects projects from build.csando files by parsing project references.
/// </summary>
public partial class ProjectDetector
{
    /// <summary>
    /// Represents a detected project in the build script.
    /// </summary>
    /// <param name="Path">Relative path to the project file.</param>
    /// <param name="Type">Type of project (Dotnet or Npm).</param>
    public record DetectedProject(string Path, ProjectType Type);

    /// <summary>
    /// Type of project detected.
    /// </summary>
    public enum ProjectType { Dotnet, Npm }

    // Regex for Dotnet.Project("path") calls.
    [GeneratedRegex(@"Dotnet\.Project\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.Compiled)]
    private static partial Regex DotnetProjectRegex();

    // Regex for Directory("path") with variable assignment.
    [GeneratedRegex(@"(?:var\s+)?(\w+)\s*=\s*Directory\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.Compiled)]
    private static partial Regex DirectoryAssignmentRegex();

    // Regex for inline Directory("path") in Npm calls.
    [GeneratedRegex(@"Npm\.\w+\s*\(\s*Directory\s*\(\s*[""']([^""']+)[""']\s*\)", RegexOptions.Compiled)]
    private static partial Regex NpmInlineDirectoryRegex();

    // Regex for Npm operations using a variable.
    [GeneratedRegex(@"Npm\.\w+\s*\(\s*(\w+)\s*[,)]", RegexOptions.Compiled)]
    private static partial Regex NpmVariableRegex();

    /// <summary>
    /// Detects all projects referenced in a build script.
    /// </summary>
    /// <param name="buildScriptPath">Path to the build.csando file.</param>
    /// <returns>List of detected projects.</returns>
    public List<DetectedProject> DetectProjects(string buildScriptPath)
    {
        if (!File.Exists(buildScriptPath))
            throw new FileNotFoundException($"Build script not found: {buildScriptPath}");

        var content = File.ReadAllText(buildScriptPath);
        var scriptDir = Path.GetDirectoryName(buildScriptPath) ?? ".";
        var projects = new List<DetectedProject>();

        // Find .NET projects.
        projects.AddRange(FindDotnetProjects(content, scriptDir));

        // Find npm projects.
        projects.AddRange(FindNpmProjects(content, scriptDir));

        return projects;
    }

    /// <summary>
    /// Finds .NET project references in the build script.
    /// </summary>
    private IEnumerable<DetectedProject> FindDotnetProjects(string content, string scriptDir)
    {
        var matches = DotnetProjectRegex().Matches(content);

        foreach (Match match in matches)
        {
            var relativePath = match.Groups[1].Value;
            var fullPath = ResolvePath(relativePath, scriptDir);

            if (File.Exists(fullPath))
            {
                yield return new DetectedProject(relativePath, ProjectType.Dotnet);
            }
        }
    }

    /// <summary>
    /// Finds npm project references (directories used with Npm operations).
    /// </summary>
    private IEnumerable<DetectedProject> FindNpmProjects(string content, string scriptDir)
    {
        var npmDirs = new HashSet<string>();

        // Find directories assigned to variables.
        var directoryAssignments = new Dictionary<string, string>();
        foreach (Match match in DirectoryAssignmentRegex().Matches(content))
        {
            var varName = match.Groups[1].Value;
            var path = match.Groups[2].Value;
            directoryAssignments[varName] = path;
        }

        // Find Npm calls using variables.
        foreach (Match match in NpmVariableRegex().Matches(content))
        {
            var varName = match.Groups[1].Value;
            if (directoryAssignments.TryGetValue(varName, out var path))
            {
                npmDirs.Add(path);
            }
        }

        // Find inline Directory() in Npm calls.
        foreach (Match match in NpmInlineDirectoryRegex().Matches(content))
        {
            npmDirs.Add(match.Groups[1].Value);
        }

        // Return directories that have package.json.
        foreach (var dir in npmDirs)
        {
            var fullDir = ResolvePath(dir, scriptDir);
            var packageJson = Path.Combine(fullDir, "package.json");

            if (File.Exists(packageJson))
            {
                yield return new DetectedProject(
                    Path.Combine(dir, "package.json"),
                    ProjectType.Npm
                );
            }
        }
    }

    /// <summary>
    /// Resolves a relative path from the build script.
    /// </summary>
    private static string ResolvePath(string relativePath, string scriptDir)
    {
        // Handle ./ prefix.
        if (relativePath.StartsWith("./"))
            relativePath = relativePath[2..];

        return Path.Combine(scriptDir, relativePath);
    }
}
