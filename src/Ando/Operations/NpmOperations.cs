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
public class NpmOperations
{
    private readonly StepRegistry _registry;
    private readonly IBuildLogger _logger;
    private readonly Func<ICommandExecutor> _executorFactory;
    private string? _workingDirectory;

    public NpmOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    {
        _registry = registry;
        _logger = logger;
        _executorFactory = executorFactory;
    }

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
        _registry.Register("Npm.Install", async () =>
        {
            var options = new CommandOptions { WorkingDirectory = _workingDirectory };
            var result = await _executorFactory().ExecuteAsync("npm", new[] { "install" }, options);
            return result.Success;
        }, _workingDirectory);
    }

    /// <summary>
    /// Runs 'npm ci' for clean install (preferred for CI).
    /// </summary>
    public void Ci()
    {
        _registry.Register("Npm.Ci", async () =>
        {
            var options = new CommandOptions { WorkingDirectory = _workingDirectory };
            var result = await _executorFactory().ExecuteAsync("npm", new[] { "ci" }, options);
            return result.Success;
        }, _workingDirectory);
    }

    /// <summary>
    /// Runs an npm script defined in package.json.
    /// </summary>
    public void Run(string scriptName)
    {
        _registry.Register($"Npm.Run({scriptName})", async () =>
        {
            var options = new CommandOptions { WorkingDirectory = _workingDirectory };
            var result = await _executorFactory().ExecuteAsync("npm", new[] { "run", scriptName }, options);
            return result.Success;
        }, _workingDirectory);
    }

    /// <summary>
    /// Runs 'npm test'.
    /// </summary>
    public void Test()
    {
        _registry.Register("Npm.Test", async () =>
        {
            var options = new CommandOptions { WorkingDirectory = _workingDirectory };
            var result = await _executorFactory().ExecuteAsync("npm", new[] { "test" }, options);
            return result.Success;
        }, _workingDirectory);
    }

    /// <summary>
    /// Runs 'npm run build'.
    /// </summary>
    public void Build()
    {
        _registry.Register("Npm.Build", async () =>
        {
            var options = new CommandOptions { WorkingDirectory = _workingDirectory };
            var result = await _executorFactory().ExecuteAsync("npm", new[] { "run", "build" }, options);
            return result.Success;
        }, _workingDirectory);
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
    public static NodeTool Use(string version)
    {
        return new NodeTool(version);
    }

    /// <summary>
    /// Uses the latest LTS version of Node.js.
    /// </summary>
    public static NodeTool UseLatestLts()
    {
        return new NodeTool("lts");
    }
}

/// <summary>
/// Represents a Node.js installation with a specific version.
/// </summary>
public class NodeTool
{
    public string Version { get; }

    public NodeTool(string version)
    {
        Version = version;
    }
}
