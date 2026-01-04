// =============================================================================
// CloudflareOperations.cs
//
// Summary: Provides Cloudflare CLI operations for build scripts.
//
// CloudflareOperations handles Cloudflare deployments through the wrangler CLI.
// Currently supports Cloudflare Pages static site deployments, with extensibility
// for future Workers and other Cloudflare services.
//
// Architecture:
// - Uses wrangler CLI (npx wrangler) for Cloudflare operations
// - Authentication via CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID env vars
// - Follows OperationsBase pattern for step registration
//
// Design Decisions:
// - Environment variables resolved at registration time (fail-fast)
// - Uses npx wrangler to avoid requiring global wrangler installation
// - Project name can come from options or environment variable
// - Separate methods for different Pages operations
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// Provides Cloudflare CLI operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class CloudflareOperations : OperationsBase
{
    // Standard Cloudflare environment variable names.
    private const string EnvApiToken = "CLOUDFLARE_API_TOKEN";
    private const string EnvAccountId = "CLOUDFLARE_ACCOUNT_ID";
    private const string EnvProjectName = "CLOUDFLARE_PROJECT_NAME";

    private string? _workingDirectory;

    public CloudflareOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
        : base(registry, logger, executorFactory)
    {
    }

    /// <summary>
    /// Sets the working directory for Cloudflare commands.
    /// Useful when the site is in a subdirectory.
    /// </summary>
    public CloudflareOperations InDirectory(string path)
    {
        _workingDirectory = path;
        return this;
    }

    /// <summary>
    /// Registers a step to verify Cloudflare credentials are configured.
    /// Fails the build if required environment variables are not set.
    /// </summary>
    public void EnsureAuthenticated()
    {
        // Validate at registration time (fail-fast).
        EnvironmentHelper.GetRequired(EnvApiToken, "Cloudflare API Token");
        EnvironmentHelper.GetRequired(EnvAccountId, "Cloudflare Account ID");

        // Register a step that verifies authentication by calling wrangler whoami.
        RegisterCommand("Cloudflare.EnsureAuthenticated", "npx",
            () => new ArgumentBuilder()
                .Add("wrangler", "whoami"),
            workingDirectory: _workingDirectory);
    }

    /// <summary>
    /// Registers a step to deploy a directory to Cloudflare Pages.
    /// </summary>
    /// <param name="directory">Directory containing the static site to deploy.</param>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void PagesDeployDirectory(string directory, Action<CloudflarePagesDeployOptions>? configure = null)
    {
        var options = new CloudflarePagesDeployOptions();
        configure?.Invoke(options);

        // Resolve project name from options or environment.
        var projectName = options.ProjectName ?? Environment.GetEnvironmentVariable(EnvProjectName);
        if (string.IsNullOrEmpty(projectName))
        {
            throw new InvalidOperationException(
                $"Cloudflare Pages project name must be provided via WithProjectName() or {EnvProjectName} environment variable.");
        }

        // Validate auth env vars at registration time.
        EnvironmentHelper.GetRequired(EnvApiToken, "Cloudflare API Token");
        EnvironmentHelper.GetRequired(EnvAccountId, "Cloudflare Account ID");

        RegisterCommand("Cloudflare.Pages.Deploy", "npx",
            () => BuildPagesDeployArgs(directory, projectName, options),
            projectName,
            _workingDirectory);
    }

    /// <summary>
    /// Registers a step to deploy the dist directory to Cloudflare Pages.
    /// Convenience method that defaults to "./dist" directory.
    /// </summary>
    /// <param name="configure">Optional configuration for deployment options.</param>
    public void PagesDeploy(Action<CloudflarePagesDeployOptions>? configure = null)
    {
        PagesDeployDirectory("./dist", configure);
    }

    /// <summary>
    /// Registers a step to list all Cloudflare Pages projects.
    /// </summary>
    public void PagesListProjects()
    {
        EnvironmentHelper.GetRequired(EnvApiToken, "Cloudflare API Token");
        EnvironmentHelper.GetRequired(EnvAccountId, "Cloudflare Account ID");

        RegisterCommand("Cloudflare.Pages.ListProjects", "npx",
            () => new ArgumentBuilder()
                .Add("wrangler", "pages", "project", "list"),
            workingDirectory: _workingDirectory);
    }

    /// <summary>
    /// Registers a step to create a new Cloudflare Pages project.
    /// </summary>
    /// <param name="projectName">Name for the new project.</param>
    /// <param name="productionBranch">Production branch name (defaults to "main").</param>
    public void PagesCreateProject(string projectName, string productionBranch = "main")
    {
        EnvironmentHelper.GetRequired(EnvApiToken, "Cloudflare API Token");
        EnvironmentHelper.GetRequired(EnvAccountId, "Cloudflare Account ID");

        RegisterCommand("Cloudflare.Pages.CreateProject", "npx",
            () => new ArgumentBuilder()
                .Add("wrangler", "pages", "project", "create", projectName)
                .Add("--production-branch", productionBranch),
            projectName,
            _workingDirectory);
    }

    /// <summary>
    /// Registers a step to list deployments for a Cloudflare Pages project.
    /// </summary>
    /// <param name="projectName">Project name. If null, uses CLOUDFLARE_PROJECT_NAME env var.</param>
    public void PagesListDeployments(string? projectName = null)
    {
        var resolvedProjectName = projectName ?? EnvironmentHelper.GetRequired(EnvProjectName, "Cloudflare Project Name");

        RegisterCommand("Cloudflare.Pages.ListDeployments", "npx",
            () => new ArgumentBuilder()
                .Add("wrangler", "pages", "deployment", "list")
                .Add("--project-name", resolvedProjectName),
            resolvedProjectName,
            _workingDirectory);
    }

    /// <summary>
    /// Builds the argument array for pages deploy command.
    /// </summary>
    private static ArgumentBuilder BuildPagesDeployArgs(
        string directory,
        string projectName,
        CloudflarePagesDeployOptions options)
    {
        return new ArgumentBuilder()
            .Add("wrangler", "pages", "deploy", directory)
            .Add("--project-name", projectName)
            .AddIfNotNull("--branch", options.Branch)
            .AddIfNotNull("--commit-hash", options.CommitHash)
            .AddIfNotNull("--commit-message", options.CommitMessage);
    }

    /// <summary>
    /// Checks if wrangler CLI is available on the system.
    /// </summary>
    /// <returns>True if wrangler is available (via npx or globally), false otherwise.</returns>
    public static bool IsWranglerAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "npx",
                Arguments = "wrangler --version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Gets installation instructions for wrangler CLI.
    /// </summary>
    /// <returns>Installation command.</returns>
    public static string GetWranglerInstallInstructions()
    {
        return "  Install: npm install -g wrangler\n  Or use npx: npx wrangler (no installation needed)";
    }
}
