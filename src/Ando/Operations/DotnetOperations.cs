// =============================================================================
// DotnetOperations.cs
//
// Summary: Provides .NET CLI operations for build scripts (restore, build, test, publish).
//
// DotnetOperations exposes the dotnet CLI commands as strongly-typed methods
// that build scripts can call. Each method registers a step in the StepRegistry
// rather than executing immediately - this enables the workflow engine to
// manage execution order, parallel execution, and error handling.
//
// Architecture:
// - Methods register steps rather than executing directly (lazy evaluation)
// - Uses executorFactory to get the current executor (local or container)
// - Options classes follow fluent builder pattern for readable configuration
// - ProjectRef provides type-safe project references
//
// Design Decisions:
// - Func<ICommandExecutor> factory allows switching executors between step registration and execution
// - Optional Action<Options> pattern for configuration is more discoverable than overloads
// - Returns void because steps are registered, not executed - success is determined at runtime
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.References;
using Ando.Steps;
using Ando.Workflow;

namespace Ando.Operations;

/// <summary>
/// Provides .NET CLI operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class DotnetOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Creates a .NET project reference from a path.
    /// Usage: var app = Dotnet.Project("./src/MyApp/MyApp.csproj");
    /// </summary>
    /// <param name="path">Path to the .csproj file.</param>
    public ProjectRef Project(string path) => ProjectRef.From(path);

    /// <summary>
    /// Registers a 'dotnet restore' step to restore NuGet packages.
    /// </summary>
    public void Restore(ProjectRef project, Action<DotnetRestoreOptions>? configure = null)
    {
        var options = new DotnetRestoreOptions();
        configure?.Invoke(options);

        RegisterCommand("Dotnet.Restore", "dotnet",
            () => new ArgumentBuilder()
                .Add("restore", project.Path)
                .AddFlag(options.NoCache, "--no-cache")
                .AddIfNotNull("-r", options.Runtime),
            project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet build' step to compile the project.
    /// </summary>
    public void Build(ProjectRef project, Action<DotnetBuildOptions>? configure = null)
    {
        var options = new DotnetBuildOptions();
        configure?.Invoke(options);

        RegisterCommand("Dotnet.Build", "dotnet",
            () => new ArgumentBuilder()
                .Add("build", project.Path)
                .AddIfNotNull("-c", options.Configuration?.ToString())
                .AddFlag(options.NoRestore, "--no-restore"),
            project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet test' step to run unit tests.
    /// </summary>
    public void Test(ProjectRef project, Action<DotnetTestOptions>? configure = null)
    {
        var options = new DotnetTestOptions();
        configure?.Invoke(options);

        RegisterCommand("Dotnet.Test", "dotnet",
            () => new ArgumentBuilder()
                .Add("test", project.Path)
                .AddIfNotNull("-c", options.Configuration?.ToString())
                .AddFlag(options.NoRestore, "--no-restore")
                .AddFlag(options.NoBuild, "--no-build")
                .AddIfNotNull("--filter", options.Filter),
            project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet publish' step to create deployment artifacts.
    /// </summary>
    public void Publish(ProjectRef project, Action<DotnetPublishOptions>? configure = null)
    {
        var options = new DotnetPublishOptions();
        configure?.Invoke(options);

        RegisterCommand("Dotnet.Publish", "dotnet",
            () => new ArgumentBuilder()
                .Add("publish", project.Path)
                .AddIfNotNull("-c", options.Configuration?.ToString())
                .AddIfNotNull("-o", options.OutputPath?.Value)
                .AddFlag(options.NoRestore, "--no-restore")
                .AddFlag(options.NoBuild, "--no-build")
                .AddIf(options.SelfContained == true, "--self-contained")
                .AddIf(options.SelfContained == false, "--no-self-contained")
                .AddIfNotNull("-r", options.Runtime)
                .AddFlag(options.SingleFile, "-p:PublishSingleFile=true"),
            project.Name);
    }

    /// <summary>
    /// Creates a reference to a .NET CLI tool for installation.
    /// </summary>
    /// <param name="packageId">NuGet package ID of the tool.</param>
    /// <param name="version">Optional specific version.</param>
    public DotnetTool Tool(string packageId, string? version = null)
    {
        return new DotnetTool(packageId, version, ExecutorFactory, Logger);
    }
}

/// <summary>
/// Represents a .NET CLI tool that can be installed and used in builds.
/// Tools are installed globally in the build environment.
/// </summary>
public class DotnetTool(
    string packageId,
    string? version,
    Func<ICommandExecutor> executorFactory,
    IBuildLogger logger)
{
    public string PackageId => packageId;
    public string? Version => version;

    private bool _installed;

    /// <summary>
    /// Ensures the tool is installed globally.
    /// Safe to call multiple times - tracks installation state.
    /// </summary>
    public async Task EnsureInstalledAsync()
    {
        if (_installed) return;

        // Log tool installation for visibility.
        var versionInfo = Version != null ? $" v{Version}" : " (latest)";
        logger.Info($"Installing .NET tool: {PackageId}{versionInfo}");

        var args = new ArgumentBuilder()
            .Add("tool", "install", PackageId, "--global")
            .AddIfNotNull("--version", Version)
            .Build();

        var result = await executorFactory().ExecuteAsync("dotnet", args);

        if (result.Success)
        {
            logger.Debug($"  Tool installed successfully: {PackageId}");
        }
        else if (result.Error?.Contains("already installed") == true)
        {
            logger.Debug($"  Tool already installed: {PackageId}");
        }
        else
        {
            logger.Warning($"  Tool installation may have failed: {PackageId}");
        }

        // Tool might already be installed, and that's fine - we mark it as installed either way.
        _installed = true;
    }
}

// Option classes for configuring dotnet CLI commands.
// These use simple properties rather than fluent builders for straightforward configuration.

/// <summary>Options for 'dotnet restore' command.</summary>
public class DotnetRestoreOptions
{
    /// <summary>Bypass the NuGet cache when restoring.</summary>
    public bool NoCache { get; set; }

    /// <summary>Target runtime identifier for runtime-specific restore.</summary>
    public string? Runtime { get; private set; }

    /// <summary>Sets the target runtime (e.g., linux-x64, win-x64, osx-arm64).</summary>
    public DotnetRestoreOptions WithRuntime(string runtime)
    {
        Runtime = runtime;
        return this;
    }
}

/// <summary>Options for 'dotnet build' command.</summary>
public class DotnetBuildOptions
{
    /// <summary>Build configuration (Debug or Release).</summary>
    public Configuration? Configuration { get; set; }

    /// <summary>Skip package restore before building.</summary>
    public bool NoRestore { get; set; }
}

/// <summary>Options for 'dotnet test' command.</summary>
public class DotnetTestOptions
{
    /// <summary>Build configuration for tests.</summary>
    public Configuration? Configuration { get; set; }

    /// <summary>Skip restore before testing.</summary>
    public bool NoRestore { get; set; }

    /// <summary>Skip build before testing.</summary>
    public bool NoBuild { get; set; }

    /// <summary>Test filter expression (e.g., "Category=Unit").</summary>
    public string? Filter { get; set; }
}
