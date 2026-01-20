// =============================================================================
// NpmOperations.cs
//
// Summary: Provides npm CLI operations for Node.js projects in build scripts.
//
// NpmOperations exposes common npm commands (install, ci, run, test, build)
// as typed methods for use in build scripts. Each method takes a DirectoryRef
// parameter to specify the working directory.
//
// Architecture:
// - Methods accept DirectoryRef as first parameter for working directory
// - Commands are registered as steps for deferred execution
// - Working directory is passed to executor via CommandOptions
//
// Example usage:
//   var frontend = Directory("./frontend");
//   Npm.Ci(frontend);
//   Npm.Run(frontend, "build");
//
// Design Decisions:
// - DirectoryRef parameter provides explicit, type-safe working directory
// - Ci() preferred over Install() in CI environments for reproducibility
// - Run() method enables executing any script from package.json
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.References;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// npm CLI operations for Node.js projects.
/// All methods take a DirectoryRef parameter to specify the working directory.
/// </summary>
public class NpmOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    NodeSdkEnsurer? nodeEnsurer = null)
    : OperationsBase(registry, logger, executorFactory)
{
    private readonly NodeSdkEnsurer? _nodeEnsurer = nodeEnsurer;

    // Helper to get the ensurer as a Func<Task>? for RegisterCommandWithEnsurer.
    private Func<Task>? GetEnsurer() =>
        _nodeEnsurer != null ? () => _nodeEnsurer.EnsureInstalledAsync() : null;
    /// <summary>
    /// Runs 'npm install' to install dependencies.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    public void Install(DirectoryRef directory)
    {
        RegisterCommandWithEnsurer("Npm.Install", "npm", ["install"],
            GetEnsurer(), directory.Name, directory.Path);
    }

    /// <summary>
    /// Runs 'npm ci' for clean install (preferred for CI).
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    public void Ci(DirectoryRef directory)
    {
        RegisterCommandWithEnsurer("Npm.Ci", "npm", ["ci"],
            GetEnsurer(), directory.Name, directory.Path);
    }

    /// <summary>
    /// Runs an npm script defined in package.json.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    /// <param name="scriptName">Name of the script to run.</param>
    public void Run(DirectoryRef directory, string scriptName)
    {
        RegisterCommandWithEnsurer($"Npm.Run({scriptName})", "npm", ["run", scriptName],
            GetEnsurer(), directory.Name, directory.Path);
    }

    /// <summary>
    /// Runs 'npm test'.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    public void Test(DirectoryRef directory)
    {
        RegisterCommandWithEnsurer("Npm.Test", "npm", ["test"],
            GetEnsurer(), directory.Name, directory.Path);
    }

    /// <summary>
    /// Runs 'npm run build'.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    public void Build(DirectoryRef directory)
    {
        RegisterCommandWithEnsurer("Npm.Build", "npm", ["run", "build"],
            GetEnsurer(), directory.Name, directory.Path);
    }

    /// <summary>
    /// Bumps the version in package.json and returns a reference to the new version.
    /// Uses 'npm version' command which handles version bumping natively.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    /// <param name="bump">Which version component to increment (default: Patch).</param>
    /// <returns>A VersionRef that resolves to the new version when the step executes.</returns>
    public VersionRef BumpVersion(DirectoryRef directory, VersionBump bump = VersionBump.Patch)
    {
        var versionRef = new VersionRef($"Npm.BumpVersion({directory.Name})");

        // Convert VersionBump to npm version argument.
        var bumpArg = bump switch
        {
            VersionBump.Major => "major",
            VersionBump.Minor => "minor",
            VersionBump.Patch => "patch",
            _ => "patch"
        };

        Registry.Register("Npm.BumpVersion", async () =>
        {
            // Ensure Node.js is installed first.
            if (_nodeEnsurer != null)
            {
                await _nodeEnsurer.EnsureInstalledAsync();
            }

            // Run npm version with --no-git-tag-version to avoid git operations.
            // This modifies package.json and returns the new version.
            var args = new[] { "version", bumpArg, "--no-git-tag-version" };
            var options = new CommandOptions { WorkingDirectory = directory.Path };

            var result = await ExecutorFactory().ExecuteAsync("npm", args, options);

            if (!result.Success)
            {
                Logger.Error($"npm version failed: {result.Error}");
                return false;
            }

            // Parse the new version from output (npm version outputs "vX.Y.Z").
            var output = result.Output?.Trim() ?? "";
            var version = output.StartsWith("v") ? output[1..] : output;

            if (string.IsNullOrEmpty(version))
            {
                // Fall back to reading from package.json.
                var packageJsonPath = Path.Combine(directory.Path, "package.json");
                if (File.Exists(packageJsonPath))
                {
                    var content = await File.ReadAllTextAsync(packageJsonPath);
                    var versionMatch = System.Text.RegularExpressions.Regex.Match(
                        content, @"""version"":\s*""(\d+\.\d+\.\d+)""");

                    if (versionMatch.Success)
                    {
                        version = versionMatch.Groups[1].Value;
                    }
                }
            }

            if (string.IsNullOrEmpty(version))
            {
                Logger.Error("Could not determine new version after npm version");
                return false;
            }

            versionRef.Resolve(version);
            Logger.Info($"Version bumped to: {version}");
            return true;
        }, directory.Name);

        return versionRef;
    }

    /// <summary>
    /// Reads the current version from package.json.
    /// Returns a VersionRef that resolves when the step executes.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    /// <returns>A VersionRef that resolves to the current version.</returns>
    public VersionRef ReadVersion(DirectoryRef directory)
    {
        var versionRef = new VersionRef($"Npm.ReadVersion({directory.Name})");

        Registry.Register("Npm.ReadVersion", async () =>
        {
            var packageJsonPath = Path.Combine(directory.Path, "package.json");
            if (!File.Exists(packageJsonPath))
            {
                Logger.Error($"package.json not found: {packageJsonPath}");
                return false;
            }

            var content = await File.ReadAllTextAsync(packageJsonPath);
            var versionMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"""version"":\s*""(\d+\.\d+\.\d+)""");

            if (!versionMatch.Success)
            {
                Logger.Error($"No version found in {packageJsonPath}");
                return false;
            }

            var version = versionMatch.Groups[1].Value;
            versionRef.Resolve(version);

            Logger.Debug($"Read version: {version}");
            return true;
        }, directory.Name);

        return versionRef;
    }
}

/// <summary>
/// Node.js tool management for installing specific versions.
/// </summary>
public static class NodeToolExtensions
{
    /// <summary>
    /// Ensures a specific Node.js version is available.
    /// In container execution, this downloads and installs Node.js.
    /// </summary>
    public static NodeTool Use(string version) => new(version);

    /// <summary>
    /// Uses the latest LTS version of Node.js.
    /// </summary>
    public static NodeTool UseLatestLts() => new("lts");
}

/// <summary>
/// Represents a Node.js installation with a specific version.
/// </summary>
public class NodeTool(string version)
{
    public string Version => version;
}
