// =============================================================================
// CloudflareOperations.cs
//
// Summary: Provides Cloudflare operations for build scripts.
//
// CloudflareOperations handles Cloudflare deployments through the wrangler CLI
// and targeted Cloudflare API calls. Currently supports Cloudflare Pages static
// site deployments, with extensibility for future Workers and other Cloudflare
// services.
//
// Architecture:
// - Uses wrangler CLI (npx wrangler) for Pages deployments
// - Uses Cloudflare REST APIs for token, project-access, and cache-purge calls
// - Authentication via CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID env vars
// - Follows OperationsBase pattern for step registration
// - Deploy methods accept DirectoryRef for explicit working directory
//
// Design Decisions:
// - Uses npx wrangler to avoid requiring global wrangler installation
// - Project name can come from options or environment variable
// - DirectoryRef parameter provides explicit, type-safe working directory
// - PagesDeploy automatically verifies project API access before deploying
//
// Authentication:
// - EnsureAuthenticated() prompts interactively if env vars are not set
// - API token input is hidden (secret) for security
// - Prompted values are set as environment variables for the current process
//   so child processes (wrangler) can use them
// - Environment variables are NOT persisted after the build ends - each run
//   will prompt again unless variables are exported in the shell
// - For CI/CD: set CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID in the
//   pipeline environment; no prompting will occur
// - For local dev: either export vars in shell or enter when prompted
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Logging;
using Ando.References;
using Ando.Steps;
using Ando.Utilities;

namespace Ando.Operations;

/// <summary>
/// Provides Cloudflare operations for build scripts.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class CloudflareOperations : OperationsBase
{
    // Standard Cloudflare environment variable names.
    private const string EnvApiToken = "CLOUDFLARE_API_TOKEN";
    private const string EnvAccountId = "CLOUDFLARE_ACCOUNT_ID";
    private const string EnvProjectName = "CLOUDFLARE_PROJECT_NAME";
    private const string EnvZoneId = "CLOUDFLARE_ZONE_ID";

    // Captured credentials to pass to Cloudflare commands in the container.
    // These are populated by authentication/preflight helpers and reused by subsequent commands.
    private string? _apiToken;
    private string? _accountId;
    private string? _zoneId;
    private readonly HashSet<string> _registeredPagesProjectChecks = new(StringComparer.OrdinalIgnoreCase);

    public CloudflareOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
        : base(registry, logger, executorFactory)
    {
    }

    /// <summary>
    /// Registers a step to verify Cloudflare credentials are configured and the API token is valid.
    /// If environment variables are not set, prompts the user for values interactively.
    /// </summary>
    public void EnsureAuthenticated()
    {
        // Get credentials from environment or prompt user interactively.
        // Capture values so they can be passed to the container.
        _apiToken = EnvironmentHelper.GetRequiredOrPrompt(EnvApiToken, "Cloudflare API Token", isSecret: true);
        _accountId = EnvironmentHelper.GetRequiredOrPrompt(EnvAccountId, "Cloudflare Account ID");

        // Keep this check on Cloudflare's token verification endpoint instead of
        // replacing it with `wrangler whoami`.
        //
        // Wrangler's `whoami` command is an identity/account-discovery command,
        // not a narrow API-token validation command. In containerized builds it
        // has failed for valid scoped API tokens because it needs broader user or
        // account-listing access than a Pages deploy token should require. That
        // creates two bad outcomes:
        // - Deployable tokens fail early before the Pages-specific preflight can run.
        // - WorkflowRunner may interpret the failed `npx wrangler whoami` step as
        //   a missing Wrangler installation and print misleading install guidance.
        //
        // The responsibilities are intentionally split:
        // - EnsureAuthenticated verifies that the token itself is accepted by
        //   Cloudflare using /user/tokens/verify.
        // - EnsurePagesProject verifies that the same token and account ID can read
        //   the target Pages project before a deploy step runs.
        // - PagesDeploy remains the only step here that needs Wrangler.
        //
        // Do not collapse these checks back into `wrangler whoami` unless Cloudflare
        // changes Wrangler to provide a token-only validation command whose required
        // permissions match /user/tokens/verify.
        RegisterCommand("Cloudflare.EnsureAuthenticated", "bash",
            BuildTokenVerifyArgs,
            environment: GetCloudflareEnvironment());
    }

    /// <summary>
    /// Registers a step to verify the current credentials can read a Cloudflare Pages project.
    /// </summary>
    /// <param name="projectName">Cloudflare Pages project name to verify.</param>
    public void EnsurePagesProject(string projectName)
    {
        var resolvedProjectName = RequireProjectName(projectName, nameof(projectName));

        EnsureCredentialsCaptured();

        if (!_registeredPagesProjectChecks.Add(resolvedProjectName))
        {
            return;
        }

        RegisterCommand("Cloudflare.Pages.EnsureProject", "bash",
            () => BuildPagesProjectVerifyArgs(resolvedProjectName),
            resolvedProjectName,
            environment: GetCloudflareEnvironment());
    }

    /// <summary>
    /// Gets the environment variables dictionary for Cloudflare commands.
    /// </summary>
    private Dictionary<string, string> GetCloudflareEnvironment()
    {
        var env = new Dictionary<string, string>();
        if (_apiToken != null)
        {
            env[EnvApiToken] = _apiToken;
        }
        if (_accountId != null)
        {
            env[EnvAccountId] = _accountId;
        }
        return env;
    }

    /// <summary>
    /// Registers a step to deploy a directory to Cloudflare Pages.
    /// The directory should be the build output (e.g., website / "dist").
    /// </summary>
    /// <param name="directory">Directory to deploy (the build output folder).</param>
    /// <param name="projectName">Cloudflare Pages project name.</param>
    public void PagesDeploy(DirectoryRef directory, string projectName)
    {
        var resolvedProjectName = RequireProjectName(projectName, nameof(projectName));

        EnsurePagesProject(resolvedProjectName);

        RegisterCommand("Cloudflare.Pages.Deploy", "npx",
            () => BuildPagesDeployArgs(".", resolvedProjectName, new CloudflarePagesDeployOptions()),
            resolvedProjectName,
            directory.Path,
            GetCloudflareEnvironment());
    }

    /// <summary>
    /// Registers a step to deploy a directory to Cloudflare Pages.
    /// Use this overload when you need additional options like branch or commit metadata.
    /// </summary>
    /// <param name="directory">Directory to deploy (the build output folder).</param>
    /// <param name="configure">Configuration for deployment options (must include project name).</param>
    public void PagesDeploy(DirectoryRef directory, Action<CloudflarePagesDeployOptions> configure)
    {
        var options = new CloudflarePagesDeployOptions();
        configure.Invoke(options);

        // Resolve project name from options or environment.
        var projectName = options.ProjectName ?? Environment.GetEnvironmentVariable(EnvProjectName);
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new InvalidOperationException(
                $"Cloudflare Pages project name must be provided via WithProjectName() or {EnvProjectName} environment variable.");
        }
        var resolvedProjectName = projectName.Trim();

        EnsurePagesProject(resolvedProjectName);

        RegisterCommand("Cloudflare.Pages.Deploy", "npx",
            () => BuildPagesDeployArgs(".", resolvedProjectName, options),
            resolvedProjectName,
            directory.Path,
            GetCloudflareEnvironment());
    }

    /// <summary>
    /// Registers a step to list all Cloudflare Pages projects.
    /// </summary>
    public void PagesListProjects()
    {
        EnsureCredentialsCaptured();

        RegisterCommand("Cloudflare.Pages.ListProjects", "npx",
            () => new ArgumentBuilder()
                .Add("wrangler", "pages", "project", "list"),
            environment: GetCloudflareEnvironment());
    }

    /// <summary>
    /// Registers a step to create a new Cloudflare Pages project.
    /// </summary>
    /// <param name="projectName">Name for the new project.</param>
    /// <param name="productionBranch">Production branch name (defaults to "main").</param>
    public void PagesCreateProject(string projectName, string productionBranch = "main")
    {
        EnsureCredentialsCaptured();

        RegisterCommand("Cloudflare.Pages.CreateProject", "npx",
            () => new ArgumentBuilder()
                .Add("wrangler", "pages", "project", "create", projectName)
                .Add("--production-branch", productionBranch),
            projectName,
            environment: GetCloudflareEnvironment());
    }

    /// <summary>
    /// Registers a step to list deployments for a Cloudflare Pages project.
    /// </summary>
    /// <param name="projectName">Project name. If null, uses CLOUDFLARE_PROJECT_NAME env var.</param>
    public void PagesListDeployments(string? projectName = null)
    {
        EnsureCredentialsCaptured();

        var resolvedProjectName = projectName ?? EnvironmentHelper.GetRequired(EnvProjectName, "Cloudflare Project Name");

        RegisterCommand("Cloudflare.Pages.ListDeployments", "npx",
            () => new ArgumentBuilder()
                .Add("wrangler", "pages", "deployment", "list")
                .Add("--project-name", resolvedProjectName),
            resolvedProjectName,
            environment: GetCloudflareEnvironment());
    }

    /// <summary>
    /// Registers a step to purge the entire Cloudflare cache for a zone.
    /// Accepts either a Zone ID or a domain name (e.g., "example.com").
    /// If a domain name is provided, it will be resolved to a Zone ID via the API.
    /// </summary>
    /// <param name="zoneIdOrDomain">Zone ID or domain name. If null, uses CLOUDFLARE_ZONE_ID env var.</param>
    public void PurgeCache(string? zoneIdOrDomain = null)
    {
        EnsureCredentialsCaptured();

        var input = zoneIdOrDomain ?? EnvironmentHelper.GetRequiredOrPrompt(EnvZoneId, "Cloudflare Zone ID or Domain");

        // Use curl to call the Cloudflare API directly since wrangler doesn't have cache purge.
        // Zone ID resolution is deferred to execution time (inside the lambda) so it happens
        // when the step runs, not when the script is parsed.
        RegisterCommand("Cloudflare.PurgeCache", "bash",
            () =>
            {
                // Resolve zone ID at execution time, not registration time.
                var resolvedZoneId = LooksLikeDomain(input) ? ResolveZoneId(input) : input;
                _zoneId = resolvedZoneId;
                var script = $"curl -X POST \"https://api.cloudflare.com/client/v4/zones/{resolvedZoneId}/purge_cache\" -H \"Authorization: Bearer $CLOUDFLARE_API_TOKEN\" -H \"Content-Type: application/json\" --data '{{\"purge_everything\":true}}' --fail-with-body --silent --show-error";

                return new ArgumentBuilder()
                    .Add("-c", script);
            },
            environment: GetCloudflareEnvironment());
    }

    /// <summary>
    /// Checks if the input looks like a domain name rather than a Zone ID.
    /// Zone IDs are 32-character hexadecimal strings.
    /// </summary>
    private static bool LooksLikeDomain(string input)
    {
        // Zone IDs are 32 hex characters with no dots.
        // Domain names contain dots and are not purely hexadecimal.
        if (!input.Contains('.'))
            return false;

        // If it contains a dot, it's likely a domain.
        return true;
    }

    /// <summary>
    /// Resolves a domain name to a Cloudflare Zone ID via the API.
    /// </summary>
    /// <param name="domain">The domain name to resolve.</param>
    /// <returns>The Zone ID for the domain.</returns>
    /// <exception cref="InvalidOperationException">If the domain cannot be found.</exception>
    private string ResolveZoneId(string domain)
    {
        Logger.Info($"Resolving Zone ID for domain: {domain}");

        try
        {
            using var client = new System.Net.Http.HttpClient();
            client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiToken}");

            var response = client.GetAsync($"https://api.cloudflare.com/client/v4/zones?name={domain}").Result;
            var content = response.Content.ReadAsStringAsync().Result;

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to lookup zone for domain '{domain}': {content}");
            }

            // Parse the JSON response to extract the zone ID.
            // Response format: {"result":[{"id":"zone-id-here",...}],...}
            var json = System.Text.Json.JsonDocument.Parse(content);
            var result = json.RootElement.GetProperty("result");

            if (result.GetArrayLength() == 0)
            {
                throw new InvalidOperationException(
                    $"No zone found for domain '{domain}'. Make sure the domain is added to your Cloudflare account.");
            }

            var zoneId = result[0].GetProperty("id").GetString()
                ?? throw new InvalidOperationException($"Zone ID is null for domain '{domain}'.");

            Logger.Info($"Resolved '{domain}' to Zone ID: {zoneId}");
            return zoneId;
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException($"Failed to resolve zone ID for domain '{domain}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Ensures credentials are captured (from environment or already set).
    /// </summary>
    private void EnsureCredentialsCaptured()
    {
        if (_apiToken == null)
        {
            _apiToken = EnvironmentHelper.GetRequired(EnvApiToken, "Cloudflare API Token");
        }
        if (_accountId == null)
        {
            _accountId = EnvironmentHelper.GetRequired(EnvAccountId, "Cloudflare Account ID");
        }
    }

    /// <summary>
    /// Builds the argument array for the Cloudflare token verification API call.
    /// </summary>
    private ArgumentBuilder BuildTokenVerifyArgs()
    {
        const string script = "curl --fail-with-body --silent --show-error -H \"Authorization: Bearer $CLOUDFLARE_API_TOKEN\" \"https://api.cloudflare.com/client/v4/user/tokens/verify\"";

        return new ArgumentBuilder()
            .Add("-c", script);
    }

    /// <summary>
    /// Builds the argument array for the Pages project verification API call.
    /// </summary>
    private ArgumentBuilder BuildPagesProjectVerifyArgs(string projectName)
    {
        var encodedProjectName = Uri.EscapeDataString(projectName);
        var script = $"curl --fail-with-body --silent --show-error -H \"Authorization: Bearer $CLOUDFLARE_API_TOKEN\" \"https://api.cloudflare.com/client/v4/accounts/$CLOUDFLARE_ACCOUNT_ID/pages/projects/{encodedProjectName}\"";

        return new ArgumentBuilder()
            .Add("-c", script);
    }

    /// <summary>
    /// Requires a non-empty Cloudflare Pages project name and trims incidental whitespace.
    /// </summary>
    private static string RequireProjectName(string projectName, string paramName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            throw new ArgumentException("Cloudflare Pages project name must be provided.", paramName);
        }

        return projectName.Trim();
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
