// =============================================================================
// NpmOperations.cs
//
// Summary: Provides npm CLI operations for Node.js projects in build scripts.
//
// NpmOperations exposes common npm commands (install, ci, run, test, build)
// as typed methods for use in build scripts. Supports both project-based and
// directory-based working directory configuration.
//
// Architecture:
// - Uses fluent InDirectory/InProject methods to set working directory
// - Commands are registered as steps for deferred execution
// - Working directory is passed to executor via CommandOptions
//
// Design Decisions:
// - Fluent pattern for directory configuration allows chaining
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
/// Use InDirectory() or InProject() to set the working directory before calling commands.
/// </summary>
public class NpmOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    private string? _workingDirectory;

    /// <summary>
    /// Sets the working directory for npm commands.
    /// </summary>
    public NpmOperations InDirectory(string path)
    {
        _workingDirectory = path;
        return this;
    }

    /// <summary>
    /// Sets the working directory for npm commands using a project reference.
    /// </summary>
    public NpmOperations InProject(ProjectRef project)
    {
        _workingDirectory = project.Directory;
        return this;
    }

    /// <summary>
    /// Runs 'npm install' to install dependencies.
    /// </summary>
    public void Install()
    {
        RegisterCommand("Npm.Install", "npm", ["install"], _workingDirectory, _workingDirectory);
    }

    /// <summary>
    /// Runs 'npm ci' for clean install (preferred for CI).
    /// </summary>
    public void Ci()
    {
        RegisterCommand("Npm.Ci", "npm", ["ci"], _workingDirectory, _workingDirectory);
    }

    /// <summary>
    /// Runs an npm script defined in package.json.
    /// </summary>
    public void Run(string scriptName)
    {
        RegisterCommand($"Npm.Run({scriptName})", "npm", ["run", scriptName], _workingDirectory, _workingDirectory);
    }

    /// <summary>
    /// Runs 'npm test'.
    /// </summary>
    public void Test()
    {
        RegisterCommand("Npm.Test", "npm", ["test"], _workingDirectory, _workingDirectory);
    }

    /// <summary>
    /// Runs 'npm run build'.
    /// </summary>
    public void Build()
    {
        RegisterCommand("Npm.Build", "npm", ["run", "build"], _workingDirectory, _workingDirectory);
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
