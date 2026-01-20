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
    DotnetSdkEnsurer? sdkEnsurer = null)
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
                .AddFlag(options.SingleFile, "-p:PublishSingleFile=true"),
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
    /// Bumps the version in a .csproj file and returns a reference to the new version.
    /// The version is updated when the step executes, and the VersionRef is resolved then.
    /// </summary>
    /// <param name="project">The project to bump the version in.</param>
    /// <param name="bump">Which version component to increment (default: Patch).</param>
    /// <returns>A VersionRef that resolves to the new version when the step executes.</returns>
    public VersionRef BumpVersion(ProjectRef project, VersionBump bump = VersionBump.Patch)
    {
        var versionRef = new VersionRef($"Dotnet.BumpVersion({project.Name})");

        Registry.Register("Dotnet.BumpVersion", async () =>
        {
            // Read the .csproj file.
            var csprojPath = project.Path;
            if (!File.Exists(csprojPath))
            {
                Logger.Error($"Project file not found: {csprojPath}");
                return false;
            }

            var content = await File.ReadAllTextAsync(csprojPath);

            // Parse current version from <Version>x.y.z</Version>.
            var versionMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<Version>(\d+)\.(\d+)\.(\d+)</Version>");

            if (!versionMatch.Success)
            {
                Logger.Error($"No <Version>x.y.z</Version> found in {csprojPath}");
                return false;
            }

            var major = int.Parse(versionMatch.Groups[1].Value);
            var minor = int.Parse(versionMatch.Groups[2].Value);
            var patch = int.Parse(versionMatch.Groups[3].Value);

            // Bump version according to specified level.
            var (newMajor, newMinor, newPatch) = bump switch
            {
                VersionBump.Major => (major + 1, 0, 0),
                VersionBump.Minor => (major, minor + 1, 0),
                VersionBump.Patch => (major, minor, patch + 1),
                _ => (major, minor, patch + 1)
            };

            var newVersion = $"{newMajor}.{newMinor}.{newPatch}";

            // Replace version in content.
            var newContent = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"<Version>\d+\.\d+\.\d+</Version>",
                $"<Version>{newVersion}</Version>");

            // Write back to file.
            await File.WriteAllTextAsync(csprojPath, newContent);

            // Resolve the version reference so subsequent steps can use it.
            versionRef.Resolve(newVersion);

            Logger.Info($"Version bumped: {major}.{minor}.{patch} → {newVersion}");
            return true;
        }, project.Name);

        return versionRef;
    }

    /// <summary>
    /// Reads the current version from a .csproj file.
    /// Returns a VersionRef that resolves when the step executes.
    /// </summary>
    /// <param name="project">The project to read the version from.</param>
    /// <returns>A VersionRef that resolves to the current version.</returns>
    public VersionRef ReadVersion(ProjectRef project)
    {
        var versionRef = new VersionRef($"Dotnet.ReadVersion({project.Name})");

        Registry.Register("Dotnet.ReadVersion", async () =>
        {
            var csprojPath = project.Path;
            if (!File.Exists(csprojPath))
            {
                Logger.Error($"Project file not found: {csprojPath}");
                return false;
            }

            var content = await File.ReadAllTextAsync(csprojPath);

            var versionMatch = System.Text.RegularExpressions.Regex.Match(
                content, @"<Version>(\d+\.\d+\.\d+)</Version>");

            if (!versionMatch.Success)
            {
                Logger.Error($"No <Version>x.y.z</Version> found in {csprojPath}");
                return false;
            }

            var version = versionMatch.Groups[1].Value;
            versionRef.Resolve(version);

            Logger.Debug($"Read version: {version}");
            return true;
        }, project.Name);

        return versionRef;
    }

    /// <summary>
    /// Installs .NET SDK globally using Microsoft's install script.
    /// Use this when building in a base image that doesn't have .NET pre-installed.
    /// Calling this method disables automatic SDK installation for subsequent operations.
    /// </summary>
    /// <param name="version">SDK version (e.g., "10.0", "9.0"). Defaults to "10.0".</param>
    public void SdkInstall(string version = "10.0")
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
