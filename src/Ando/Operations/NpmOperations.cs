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

namespace Ando.Operations;

/// <summary>
/// npm CLI operations for Node.js projects.
/// All methods take a DirectoryRef parameter to specify the working directory.
/// </summary>
public class NpmOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Runs 'npm install' to install dependencies.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    public void Install(DirectoryRef directory)
    {
        RegisterCommand("Npm.Install", "npm", ["install"], directory.Name, directory.Path);
    }

    /// <summary>
    /// Runs 'npm ci' for clean install (preferred for CI).
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    public void Ci(DirectoryRef directory)
    {
        RegisterCommand("Npm.Ci", "npm", ["ci"], directory.Name, directory.Path);
    }

    /// <summary>
    /// Runs an npm script defined in package.json.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    /// <param name="scriptName">Name of the script to run.</param>
    public void Run(DirectoryRef directory, string scriptName)
    {
        RegisterCommand($"Npm.Run({scriptName})", "npm", ["run", scriptName], directory.Name, directory.Path);
    }

    /// <summary>
    /// Runs 'npm test'.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    public void Test(DirectoryRef directory)
    {
        RegisterCommand("Npm.Test", "npm", ["test"], directory.Name, directory.Path);
    }

    /// <summary>
    /// Runs 'npm run build'.
    /// </summary>
    /// <param name="directory">Directory containing package.json.</param>
    public void Build(DirectoryRef directory)
    {
        RegisterCommand("Npm.Build", "npm", ["run", "build"], directory.Name, directory.Path);
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
