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
public class DotnetOperations
{
    private readonly StepRegistry _registry;
    private readonly IBuildLogger _logger;
    private readonly Func<ICommandExecutor> _executorFactory;

    public DotnetOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    {
        _registry = registry;
        _logger = logger;
        // Factory pattern: executor is obtained at step execution time, not registration time.
        // This allows the build context to switch executors (e.g., from local to container).
        _executorFactory = executorFactory;
    }

    /// <summary>
    /// Registers a 'dotnet restore' step to restore NuGet packages.
    /// </summary>
    public void Restore(ProjectRef project, Action<DotnetRestoreOptions>? configure = null)
    {
        var options = new DotnetRestoreOptions();
        configure?.Invoke(options);

        _registry.Register("Dotnet.Restore", async () =>
        {
            var args = new List<string> { "restore", project.Path };

            if (options.NoCache)
            {
                args.Add("--no-cache");
            }

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet build' step to compile the project.
    /// </summary>
    public void Build(ProjectRef project, Action<DotnetBuildOptions>? configure = null)
    {
        var options = new DotnetBuildOptions();
        configure?.Invoke(options);

        _registry.Register("Dotnet.Build", async () =>
        {
            var args = new List<string> { "build", project.Path };

            // Add configuration (Debug/Release) if specified.
            if (options.Configuration != null)
            {
                args.AddRange(new[] { "-c", options.Configuration.ToString()! });
            }

            // Skip restore if already done in a previous step.
            if (options.NoRestore)
            {
                args.Add("--no-restore");
            }

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet test' step to run unit tests.
    /// </summary>
    public void Test(ProjectRef project, Action<DotnetTestOptions>? configure = null)
    {
        var options = new DotnetTestOptions();
        configure?.Invoke(options);

        _registry.Register("Dotnet.Test", async () =>
        {
            var args = new List<string> { "test", project.Path };

            if (options.Configuration != null)
            {
                args.AddRange(new[] { "-c", options.Configuration.ToString()! });
            }

            if (options.NoRestore)
            {
                args.Add("--no-restore");
            }

            if (options.NoBuild)
            {
                args.Add("--no-build");
            }

            if (options.Filter != null)
            {
                args.AddRange(new[] { "--filter", options.Filter });
            }

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet publish' step to create deployment artifacts.
    /// </summary>
    public void Publish(ProjectRef project, Action<DotnetPublishOptions>? configure = null)
    {
        var options = new DotnetPublishOptions();
        configure?.Invoke(options);

        _registry.Register("Dotnet.Publish", async () =>
        {
            var args = new List<string> { "publish", project.Path };

            if (options.Configuration != null)
            {
                args.AddRange(new[] { "-c", options.Configuration.ToString()! });
            }

            // Output path for published artifacts.
            if (options.OutputPath != null)
            {
                args.AddRange(new[] { "-o", options.OutputPath.Value });
            }

            if (options.NoRestore)
            {
                args.Add("--no-restore");
            }

            if (options.NoBuild)
            {
                args.Add("--no-build");
            }

            // Self-contained includes the .NET runtime with the app.
            if (options.SelfContained.HasValue)
            {
                args.Add(options.SelfContained.Value ? "--self-contained" : "--no-self-contained");
            }

            // Target runtime identifier (e.g., linux-x64, win-x64, osx-arm64).
            if (options.Runtime != null)
            {
                args.AddRange(new[] { "-r", options.Runtime });
            }

            // Single file publishes all assemblies into one executable.
            if (options.SingleFile)
            {
                args.Add("-p:PublishSingleFile=true");
            }

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, project.Name);
    }

    /// <summary>
    /// Creates a reference to a .NET CLI tool for installation.
    /// </summary>
    /// <param name="packageId">NuGet package ID of the tool.</param>
    /// <param name="version">Optional specific version.</param>
    public DotnetTool Tool(string packageId, string? version = null)
    {
        return new DotnetTool(packageId, version, _registry, _executorFactory, _logger);
    }
}

/// <summary>
/// Represents a .NET CLI tool that can be installed and used in builds.
/// Tools are installed globally in the build environment.
/// </summary>
public class DotnetTool
{
    public string PackageId { get; }
    public string? Version { get; }

    private readonly StepRegistry _registry;
    private readonly Func<ICommandExecutor> _executorFactory;
    private readonly IBuildLogger _logger;
    private bool _installed;

    public DotnetTool(string packageId, string? version, StepRegistry registry, Func<ICommandExecutor> executorFactory, IBuildLogger logger)
    {
        PackageId = packageId;
        Version = version;
        _registry = registry;
        _executorFactory = executorFactory;
        _logger = logger;
    }

    /// <summary>
    /// Ensures the tool is installed globally.
    /// Safe to call multiple times - tracks installation state.
    /// </summary>
    public async Task EnsureInstalledAsync()
    {
        if (_installed) return;

        // Log tool installation for visibility.
        var versionInfo = Version != null ? $" v{Version}" : " (latest)";
        _logger.Info($"Installing .NET tool: {PackageId}{versionInfo}");

        var args = new List<string> { "tool", "install", PackageId, "--global" };
        if (Version != null)
        {
            args.AddRange(new[] { "--version", Version });
        }

        var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());

        if (result.Success)
        {
            _logger.Debug($"  Tool installed successfully: {PackageId}");
        }
        else
        {
            // Tool might already be installed - check if it's a "already installed" message.
            if (result.Error?.Contains("already installed") == true)
            {
                _logger.Debug($"  Tool already installed: {PackageId}");
            }
            else
            {
                _logger.Warning($"  Tool installation may have failed: {PackageId}");
            }
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
