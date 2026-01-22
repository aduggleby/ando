// =============================================================================
// PlaywrightOperations.cs
//
// Summary: Provides Playwright CLI operations for E2E testing in build scripts.
//
// PlaywrightOperations exposes Playwright commands (test, install) as typed
// methods for use in build scripts. Each method takes a DirectoryRef parameter
// to specify the working directory.
//
// Architecture:
// - Methods accept DirectoryRef as first parameter for working directory
// - Commands are registered as steps for deferred execution
// - Supports both `npx playwright test` and npm script patterns
// - Working directory is passed to executor via CommandOptions
//
// Example usage:
//   var e2e = Directory("./tests/E2E");
//   Npm.Ci(e2e);
//   Playwright.Install(e2e);
//   Playwright.Test(e2e);
//
// Design Decisions:
// - Separate class from NpmOperations for cleaner API with Playwright-specific options
// - Supports both npx playwright and npm script patterns for flexibility
// - Test options mirror common Playwright CLI flags (headed, project, workers, etc.)
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.References;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// Playwright CLI operations for E2E testing.
/// All methods take a DirectoryRef parameter to specify the working directory.
/// </summary>
public class PlaywrightOperations(
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
    /// Runs Playwright tests.
    /// By default uses 'npx playwright test'. Set UseNpmScript to use 'npm run test' instead.
    /// </summary>
    /// <param name="directory">Directory containing playwright.config.ts or package.json.</param>
    /// <param name="configure">Optional callback to configure test options.</param>
    public void Test(DirectoryRef directory, Action<PlaywrightTestOptions>? configure = null)
    {
        var options = new PlaywrightTestOptions();
        configure?.Invoke(options);

        if (options.UseNpmScript)
        {
            // Use npm run <script>
            var scriptName = options.NpmScriptName ?? "test";
            RegisterCommandWithEnsurer($"Playwright.Test({scriptName})", "npm",
                () => new ArgumentBuilder()
                    .Add("run", scriptName),
                GetEnsurer(), directory.Name, directory.Path);
        }
        else
        {
            // Use npx playwright test
            RegisterCommandWithEnsurer("Playwright.Test", "npx",
                () => new ArgumentBuilder()
                    .Add("playwright", "test")
                    .AddIfNotNull("--project", options.Project)
                    .AddFlag(options.Headed, "--headed")
                    .AddFlag(options.UI, "--ui")
                    .AddIfNotNull("--workers", options.Workers?.ToString())
                    .AddIfNotNull("--reporter", options.Reporter)
                    .AddIfNotNull("--grep", options.Grep)
                    .AddFlag(options.UpdateSnapshots, "--update-snapshots"),
                GetEnsurer(), directory.Name, directory.Path);
        }
    }

    /// <summary>
    /// Installs Playwright browsers and system dependencies via 'npx playwright install --with-deps'.
    /// Call this after npm install and before running tests.
    /// The --with-deps flag installs required system packages (libgtk, libasound, etc.).
    /// </summary>
    /// <param name="directory">Directory containing playwright.config.ts.</param>
    public void Install(DirectoryRef directory)
    {
        RegisterCommandWithEnsurer("Playwright.Install", "npx",
            ["playwright", "install", "--with-deps"],
            GetEnsurer(), directory.Name, directory.Path);
    }
}

/// <summary>
/// Options for Playwright test runs.
/// </summary>
public class PlaywrightTestOptions
{
    /// <summary>
    /// Run tests for a specific project (e.g., "chromium", "firefox", "webkit").
    /// Maps to --project flag.
    /// </summary>
    public string? Project { get; set; }

    /// <summary>
    /// Run tests in headed mode (visible browser windows).
    /// Maps to --headed flag.
    /// </summary>
    public bool Headed { get; set; }

    /// <summary>
    /// Run tests in UI mode for interactive debugging.
    /// Maps to --ui flag.
    /// </summary>
    public bool UI { get; set; }

    /// <summary>
    /// Number of parallel workers. Defaults to half of CPU cores.
    /// Maps to --workers flag.
    /// </summary>
    public int? Workers { get; set; }

    /// <summary>
    /// Reporter to use (e.g., "html", "list", "dot", "json").
    /// Maps to --reporter flag.
    /// </summary>
    public string? Reporter { get; set; }

    /// <summary>
    /// Filter tests by title pattern.
    /// Maps to --grep flag.
    /// </summary>
    public string? Grep { get; set; }

    /// <summary>
    /// Update visual snapshots.
    /// Maps to --update-snapshots flag.
    /// </summary>
    public bool UpdateSnapshots { get; set; }

    /// <summary>
    /// Use 'npm run' instead of 'npx playwright test'.
    /// Useful when playwright is configured in package.json scripts.
    /// </summary>
    public bool UseNpmScript { get; set; }

    /// <summary>
    /// Custom npm script name when UseNpmScript is true.
    /// Defaults to "test" if not specified.
    /// </summary>
    public string? NpmScriptName { get; set; }
}
