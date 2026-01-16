// =============================================================================
// NugetOperations.cs
//
// Summary: Provides NuGet package operations for build scripts (pack, push).
//
// NugetOperations exposes NuGet commands as strongly-typed methods that build
// scripts can call. Each method registers a step in the StepRegistry rather
// than executing immediately - this enables the workflow engine to manage
// execution order, parallel execution, and error handling.
//
// Architecture:
// - Methods register steps rather than executing directly (lazy evaluation)
// - Uses executorFactory to get the current executor (local or container)
// - Options classes follow fluent builder pattern for readable configuration
// - ProjectRef provides type-safe project references
//
// Design Decisions:
// - Pack uses 'dotnet pack' to create NuGet packages
// - Push uses 'dotnet nuget push' to publish packages to NuGet feeds
// - API key is obtained via EnsureAuthenticated() which prompts if not set
// - Default source is nuget.org but can be configured for private feeds
//
// Authentication:
// - EnsureAuthenticated() prompts interactively if NUGET_API_KEY env var is not set
// - API key input is hidden (secret) for security
// - Prompted value is set as environment variable for the current process
//   so child processes (dotnet nuget push) can use it
// - Environment variable is NOT persisted after the build ends
// - For CI/CD: set NUGET_API_KEY in the pipeline environment
// - For local dev: either export var in shell or enter when prompted
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.References;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// Provides NuGet package operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class NugetOperations : OperationsBase
{
    // Standard NuGet environment variable name.
    private const string EnvApiKey = "NUGET_API_KEY";

    // Captured API key to pass to commands.
    // Populated by EnsureAuthenticated() and used by subsequent push commands.
    private string? _apiKey;

    public NugetOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
        : base(registry, logger, executorFactory)
    {
    }

    /// <summary>
    /// Ensures NuGet API key is available for publishing.
    /// If NUGET_API_KEY environment variable is not set, prompts the user interactively.
    /// Call this before Push operations.
    /// </summary>
    public void EnsureAuthenticated()
    {
        // Get API key from environment or prompt user interactively.
        // The key is captured so it can be passed to push commands.
        _apiKey = EnvironmentHelper.GetRequiredOrPrompt(EnvApiKey, "NuGet API Key", isSecret: true);

        // Log success (without revealing the key).
        Logger.Info("NuGet API key configured.");
    }

    /// <summary>
    /// Registers a 'dotnet pack' step to create a NuGet package.
    /// </summary>
    /// <param name="project">The project to pack.</param>
    /// <param name="configure">Optional configuration for pack options.</param>
    public void Pack(ProjectRef project, Action<NugetPackOptions>? configure = null)
    {
        var options = new NugetPackOptions();
        configure?.Invoke(options);

        RegisterCommand("Nuget.Pack", "dotnet",
            () => new ArgumentBuilder()
                .Add("pack", project.Path)
                .AddIfNotNull("-c", options.Configuration?.ToString())
                .AddIfNotNull("-o", options.OutputPath?.Value)
                .AddFlag(options.NoRestore, "--no-restore")
                .AddFlag(options.NoBuild, "--no-build")
                .AddFlag(options.IncludeSymbols, "--include-symbols")
                .AddFlag(options.IncludeSource, "--include-source")
                .AddIfNotNull("-p:PackageVersion", options.Version)
                .AddIfNotNull("-p:VersionSuffix", options.VersionSuffix),
            project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet nuget push' step to publish packages for a project.
    /// Looks for *.nupkg files in the project's output directory.
    /// Call EnsureAuthenticated() first to set up the API key.
    /// </summary>
    /// <param name="project">The project whose packages to push.</param>
    /// <param name="configure">Optional configuration for push options.</param>
    public void Push(ProjectRef project, Action<NugetPushOptions>? configure = null)
    {
        var options = new NugetPushOptions();
        configure?.Invoke(options);

        EnsureApiKeyCaptured();

        // Get the project directory and look for packages there.
        var projectDir = Path.GetDirectoryName(project.Path) ?? ".";
        var packagePattern = Path.Combine(projectDir, "bin", "Release", "*.nupkg");

        RegisterCommand("Nuget.Push", "dotnet",
            () => new ArgumentBuilder()
                .Add("nuget", "push", packagePattern)
                .AddIfNotNull("--source", options.Source ?? NugetPushOptions.NuGetOrgSource)
                .AddIfNotNull("--api-key", GetEffectiveApiKey(options))
                .AddFlag(options.SkipDuplicate, "--skip-duplicate")
                .AddFlag(options.NoSymbols, "--no-symbols"),
            project.Name);
    }

    /// <summary>
    /// Registers a 'dotnet nuget push' step to publish a NuGet package.
    /// Call EnsureAuthenticated() first to set up the API key.
    /// </summary>
    /// <param name="packagePath">Path to the .nupkg file or glob pattern (e.g., *.nupkg).</param>
    /// <param name="configure">Optional configuration for push options.</param>
    public void Push(string packagePath, Action<NugetPushOptions>? configure = null)
    {
        var options = new NugetPushOptions();
        configure?.Invoke(options);

        EnsureApiKeyCaptured();

        RegisterCommand("Nuget.Push", "dotnet",
            () => new ArgumentBuilder()
                .Add("nuget", "push", packagePath)
                .AddIfNotNull("--source", options.Source ?? NugetPushOptions.NuGetOrgSource)
                .AddIfNotNull("--api-key", GetEffectiveApiKey(options))
                .AddFlag(options.SkipDuplicate, "--skip-duplicate")
                .AddFlag(options.NoSymbols, "--no-symbols"),
            Path.GetFileName(packagePath));
    }

    /// <summary>
    /// Gets the effective API key - from options if explicitly set, otherwise from captured value.
    /// </summary>
    private string? GetEffectiveApiKey(NugetPushOptions options)
    {
        // Options take precedence if explicitly set.
        if (!string.IsNullOrEmpty(options.ApiKey))
        {
            return options.ApiKey;
        }

        // Otherwise use the captured key from EnsureAuthenticated().
        return _apiKey;
    }

    /// <summary>
    /// Ensures API key is captured (from environment or already set via EnsureAuthenticated).
    /// </summary>
    private void EnsureApiKeyCaptured()
    {
        if (_apiKey == null)
        {
            _apiKey = EnvironmentHelper.Get(EnvApiKey);
        }
    }
}
