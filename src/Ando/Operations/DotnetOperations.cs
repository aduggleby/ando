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
using Ando.Utilities;
using Ando.Versioning;
using Ando.Workflow;

namespace Ando.Operations;

/// <summary>
/// Provides .NET CLI operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class DotnetOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory,
    DotnetSdkEnsurer? sdkEnsurer = null,
    RunSession? session = null)
    : OperationsBase(registry, logger, executorFactory)
{
    private readonly DotnetSdkEnsurer? _sdkEnsurer = sdkEnsurer;

    // Helper to get the ensurer as a Func<Task>? for RegisterCommandWithEnsurer.
    private Func<Task>? GetEnsurer() =>
        _sdkEnsurer != null ? () => _sdkEnsurer.EnsureInstalledAsync() : null;
    /// <summary>
    /// Creates a .NET project reference from a path.
    /// Usage: var app = Dotnet.Project("./src/MyApp/MyApp.csproj");
    /// </summary>
    /// <param name="path">Path to the .csproj file.</param>
    public ProjectRef Project(string path) => ProjectRef.From(path);

    /// <summary>
    /// Reads the literal &lt;Version&gt; property from an MSBuild props/csproj file, on the host,
    /// when the script loads (not as a step). Uses the same parser as `ando bump`: the file must
    /// have exactly one &lt;Version&gt; element directly inside a &lt;PropertyGroup&gt;. It does not
    /// evaluate MSBuild (no imports, conditions or property references such as $(Other)).
    /// A relative path is resolved against the source root (the --ref worktree when set).
    /// Throws a clear error when the file is missing, the version is empty or there is not
    /// exactly one &lt;Version&gt;.
    /// Usage: var version = Dotnet.ReadVersion();  // Directory.Build.props
    /// </summary>
    /// <param name="path">Path to the file (default "Directory.Build.props").</param>
    public string ReadVersion(string path = "Directory.Build.props")
    {
        var full = Path.GetFullPath(path, session?.SourceRoot ?? Environment.CurrentDirectory);
        if (!File.Exists(full))
        {
            throw new FileNotFoundException($"Dotnet.ReadVersion: version file not found: {full}");
        }

        var version = MsBuildVersionFile.Read(full);
        if (string.IsNullOrWhiteSpace(version))
        {
            throw new InvalidOperationException($"Dotnet.ReadVersion: the <Version> in {full} is empty.");
        }
        return version;
    }

    /// <summary>
    /// Registers a 'dotnet restore' step to restore NuGet packages.
    /// </summary>
    public void Restore(ProjectRef project, Action<DotnetRestoreOptions>? configure = null)
    {
        var options = new DotnetRestoreOptions();
        configure?.Invoke(options);

        RegisterCommandWithEnsurer("Dotnet.Restore", "dotnet",
            () => new ArgumentBuilder()
                .Add("restore", project.Path)
                .AddFlag(options.NoCache, "--no-cache")
                .AddIfNotNull("-r", options.Runtime),
            GetEnsurer(),
            project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet build' step to compile the project.
    /// </summary>
    public void Build(ProjectRef project, Action<DotnetBuildOptions>? configure = null)
    {
        var options = new DotnetBuildOptions();
        configure?.Invoke(options);

        RegisterCommandWithEnsurer("Dotnet.Build", "dotnet",
            () => new ArgumentBuilder()
                .Add("build", project.Path)
                .AddIfNotNull("-c", options.Configuration?.ToString())
                .AddFlag(options.NoRestore, "--no-restore"),
            GetEnsurer(),
            project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet test' step to run unit tests.
    /// </summary>
    public void Test(ProjectRef project, Action<DotnetTestOptions>? configure = null)
    {
        var options = new DotnetTestOptions();
        configure?.Invoke(options);

        RegisterCommandWithEnsurer("Dotnet.Test", "dotnet",
            () => new ArgumentBuilder()
                .Add("test", project.Path)
                .AddIfNotNull("-c", options.Configuration?.ToString())
                .AddFlag(options.NoRestore, "--no-restore")
                .AddFlag(options.NoBuild, "--no-build")
                .AddIfNotNull("--filter", options.Filter),
            GetEnsurer(),
            project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet publish' step to create deployment artifacts.
    /// </summary>
    public void Publish(ProjectRef project, Action<DotnetPublishOptions>? configure = null)
    {
        var options = new DotnetPublishOptions();
        configure?.Invoke(options);

        RegisterCommandWithEnsurer("Dotnet.Publish", "dotnet",
            () => new ArgumentBuilder()
                .Add("publish", project.Path)
                .AddIfNotNull("-c", options.Configuration?.ToString())
                .AddIfNotNull("-o", options.OutputPath?.Value)
                .AddFlag(options.NoRestore, "--no-restore")
                .AddFlag(options.NoBuild, "--no-build")
                .AddIf(options.SelfContained == true, "--self-contained")
                .AddIf(options.SelfContained == false, "--no-self-contained")
                .AddIfNotNull("-r", options.Runtime)
                .AddFlag(options.SingleFile, "-p:PublishSingleFile=true")
                .AddFlag(options.NoLogo, "--nologo"),
            GetEnsurer(),
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

    /// <summary>
    /// Installs .NET SDK globally using Microsoft's install script.
    /// Use this when building in a base image that doesn't have .NET pre-installed.
    /// Calling this method disables automatic SDK installation for subsequent operations.
    /// </summary>
    /// <param name="version">SDK version (e.g., "9.0", "8.0"). Defaults to "9.0".</param>
    public void SdkInstall(string version = "9.0")
    {
        // Mark manual install to disable auto-install for subsequent operations.
        _sdkEnsurer?.MarkManualInstallCalled();

        // Install .NET SDK via Microsoft's official install script.
        // First checks if the correct version is already installed (for warm containers).
        // If not, installs dependencies (curl, ca-certificates, libicu for globalization),
        // then runs the install script.
        // Creates symlink in /usr/local/bin so dotnet is available in PATH for subsequent commands.
        RegisterCommand("Dotnet.SdkInstall", "bash",
            () => new ArgumentBuilder()
                .Add("-c")
                .Add($"if command -v dotnet >/dev/null && dotnet --version | grep -q '^{version}\\.'; then " +
                     $"echo '.NET SDK {version} already installed'; " +
                     $"else " +
                     $"apt-get update && apt-get install -y curl ca-certificates libicu70 && " +
                     $"curl -fsSL https://dot.net/v1/dotnet-install.sh | bash -s -- --channel {version} && " +
                     "ln -sf $HOME/.dotnet/dotnet /usr/local/bin/dotnet; " +
                     $"fi"),
            $"v{version}");
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
