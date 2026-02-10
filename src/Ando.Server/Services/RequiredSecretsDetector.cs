// =============================================================================
// RequiredSecretsDetector.cs
//
// Summary: Detects required environment variables from build scripts.
//
// Parses build.csando files to find Env() calls and operation patterns that
// indicate required secrets. This enables automatic detection of what secrets
// a project needs before builds can run.
//
// Patterns detected:
// - Env("VAR_NAME") - required by default
// - Env("VAR_NAME", required: true)
// - Env["VAR_NAME"] - indexer access is also required
// - Nuget.EnsureAuthenticated() -> NUGET_API_KEY
// - Cloudflare.EnsureAuthenticated() -> CLOUDFLARE_API_TOKEN, CLOUDFLARE_ACCOUNT_ID
// - Docker.Build(...WithTag("ghcr.io/...")...WithPush()) -> GITHUB_TOKEN (PAT with write:packages)
// - GitHub.PushImage(...) -> GITHUB_TOKEN (PAT with write:packages)
// - Ando.Build(Directory("subdir")) - recursive detection from sub-builds
//
// Design Decisions:
// - Uses regex for parsing (simpler than Roslyn for this use case)
// - Returns distinct, sorted list for consistency
// - Logs warnings but doesn't fail on parse errors
// =============================================================================

using System.Text.RegularExpressions;
using Ando.Server.GitHub;

namespace Ando.Server.Services;

/// <summary>
/// Detects required environment variables from build scripts.
/// </summary>
public partial class RequiredSecretsDetector : IRequiredSecretsDetector
{
    private readonly IGitHubService _gitHubService;
    private readonly ILogger<RequiredSecretsDetector> _logger;

    // -------------------------------------------------------------------------
    // Regex patterns for detecting environment variable usage
    // -------------------------------------------------------------------------

    // Matches: Env("VAR_NAME") or Env("VAR_NAME", ...)
    [GeneratedRegex(@"Env\s*\(\s*""([A-Za-z_][A-Za-z0-9_]*)""\s*(?:,\s*[^)]+)?\)", RegexOptions.Compiled)]
    private static partial Regex EnvCallPattern();

    // Matches: Env["VAR_NAME"] - indexer access
    [GeneratedRegex(@"Env\s*\[\s*""([A-Za-z_][A-Za-z0-9_]*)""\s*\]", RegexOptions.Compiled)]
    private static partial Regex EnvIndexerPattern();

    // Matches: Nuget.EnsureAuthenticated()
    [GeneratedRegex(@"Nuget\s*\.\s*EnsureAuthenticated\s*\(\s*\)", RegexOptions.Compiled)]
    private static partial Regex NugetEnsureAuthPattern();

    // Matches: Cloudflare.EnsureAuthenticated()
    [GeneratedRegex(@"Cloudflare\s*\.\s*EnsureAuthenticated\s*\(\s*\)", RegexOptions.Compiled)]
    private static partial Regex CloudflareEnsureAuthPattern();

    // Matches: Azure.EnsureAuthenticated()
    [GeneratedRegex(@"Azure\s*\.\s*EnsureAuthenticated\s*\(\s*\)", RegexOptions.Compiled)]
    private static partial Regex AzureEnsureAuthPattern();

    // Matches: GitHub.EnsureAuthenticated()
    [GeneratedRegex(@"GitHub\s*\.\s*EnsureAuthenticated\s*\(\s*\)", RegexOptions.Compiled)]
    private static partial Regex GitHubEnsureAuthPattern();

    // -------------------------------------------------------------------------
    // Container registry (ghcr.io) patterns
    // -------------------------------------------------------------------------

    // Matches: GitHub.PushImage("name", ...)
    [GeneratedRegex(@"GitHub\s*\.\s*PushImage\s*\(", RegexOptions.Compiled)]
    private static partial Regex GitHubPushImagePattern();

    // -------------------------------------------------------------------------
    // Regex patterns for sub-build detection
    // -------------------------------------------------------------------------

    // Matches: Ando.Build(Directory("subdir")) or Ando.Build(Directory("./subdir"))
    [GeneratedRegex(@"Ando\s*\.\s*Build\s*\(\s*Directory\s*\(\s*""([^""]+)""\s*\)\s*\)", RegexOptions.Compiled)]
    private static partial Regex AndoBuildPattern();

    // Matches: Build("subdir") for backward compatibility
    [GeneratedRegex(@"(?<!Ando\s*\.\s*)Build\s*\(\s*""([^""]+)""\s*\)", RegexOptions.Compiled)]
    private static partial Regex LegacyBuildPattern();

    // -------------------------------------------------------------------------
    // Known operation secrets mappings
    // -------------------------------------------------------------------------

    /// <summary>
    /// Maps operation authentication methods to their required environment variables.
    /// </summary>
    private static readonly Dictionary<string, string[]> OperationSecrets = new()
    {
        ["Nuget.EnsureAuthenticated"] = ["NUGET_API_KEY"],
        ["Cloudflare.EnsureAuthenticated"] = ["CLOUDFLARE_API_TOKEN", "CLOUDFLARE_ACCOUNT_ID"],
        ["Azure.EnsureAuthenticated"] = ["AZURE_CLIENT_ID", "AZURE_CLIENT_SECRET", "AZURE_TENANT_ID"],
        ["GitHub.EnsureAuthenticated"] = ["GITHUB_TOKEN"],
    };

    public RequiredSecretsDetector(IGitHubService gitHubService, ILogger<RequiredSecretsDetector> logger)
    {
        _gitHubService = gitHubService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> DetectRequiredSecretsAsync(
        long installationId,
        string repoFullName,
        string? branch = null)
    {
        var allSecrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Fetch main build.csando
        var mainContent = await _gitHubService.GetFileContentAsync(
            installationId,
            repoFullName,
            "build.csando",
            branch);

        if (string.IsNullOrEmpty(mainContent))
        {
            _logger.LogDebug("No build.csando found in {Repo}", repoFullName);
            return [];
        }

        // Parse main script
        var mainSecrets = ParseRequiredSecrets(mainContent);
        foreach (var secret in mainSecrets)
        {
            allSecrets.Add(secret);
        }

        // Find sub-builds and parse them recursively
        var subBuilds = ParseSubBuilds(mainContent);
        foreach (var subBuildPath in subBuilds)
        {
            var subBuildScriptPath = $"{subBuildPath}/build.csando";
            var subContent = await _gitHubService.GetFileContentAsync(
                installationId,
                repoFullName,
                subBuildScriptPath,
                branch);

            if (!string.IsNullOrEmpty(subContent))
            {
                _logger.LogDebug("Parsing sub-build script: {Path}", subBuildScriptPath);
                var subSecrets = ParseRequiredSecrets(subContent);
                foreach (var secret in subSecrets)
                {
                    allSecrets.Add(secret);
                }
            }
        }

        _logger.LogInformation(
            "Detected {Count} required secrets in {Repo}: {Secrets}",
            allSecrets.Count,
            repoFullName,
            string.Join(", ", allSecrets.Order()));

        return allSecrets.Order().ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> ParseRequiredSecrets(string scriptContent)
    {
        var secrets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Find Env("VAR_NAME") calls
        var envMatches = EnvCallPattern().Matches(scriptContent);
        foreach (Match match in envMatches)
        {
            var varName = match.Groups[1].Value;

            // Skip if explicitly marked as not required (required: false)
            if (!match.Value.Contains("required: false", StringComparison.OrdinalIgnoreCase))
            {
                secrets.Add(varName);
            }
        }

        // Find Env["VAR_NAME"] indexer access
        var indexerMatches = EnvIndexerPattern().Matches(scriptContent);
        foreach (Match match in indexerMatches)
        {
            secrets.Add(match.Groups[1].Value);
        }

        // Check for Nuget.EnsureAuthenticated()
        if (NugetEnsureAuthPattern().IsMatch(scriptContent))
        {
            foreach (var secret in OperationSecrets["Nuget.EnsureAuthenticated"])
            {
                secrets.Add(secret);
            }
        }

        // Check for Cloudflare.EnsureAuthenticated()
        if (CloudflareEnsureAuthPattern().IsMatch(scriptContent))
        {
            foreach (var secret in OperationSecrets["Cloudflare.EnsureAuthenticated"])
            {
                secrets.Add(secret);
            }
        }

        // Check for Azure.EnsureAuthenticated()
        if (AzureEnsureAuthPattern().IsMatch(scriptContent))
        {
            foreach (var secret in OperationSecrets["Azure.EnsureAuthenticated"])
            {
                secrets.Add(secret);
            }
        }

        // Check for GitHub.EnsureAuthenticated()
        if (GitHubEnsureAuthPattern().IsMatch(scriptContent))
        {
            foreach (var secret in OperationSecrets["GitHub.EnsureAuthenticated"])
            {
                secrets.Add(secret);
            }
        }

        // GitHub.PushImage always pushes to ghcr.io and requires a token that can write packages.
        if (GitHubPushImagePattern().IsMatch(scriptContent))
        {
            secrets.Add("GITHUB_TOKEN");
        }

        // Docker.Build only requires GHCR auth when pushing to ghcr.io. Mirror the runtime behavior:
        // DockerOperations triggers ghcr login when Push=true AND at least one tag contains "ghcr.io".
        if (scriptContent.Contains("Docker.Build", StringComparison.OrdinalIgnoreCase) &&
            scriptContent.Contains("WithPush", StringComparison.OrdinalIgnoreCase) &&
            scriptContent.Contains("ghcr.io/", StringComparison.OrdinalIgnoreCase))
        {
            secrets.Add("GITHUB_TOKEN");
        }

        return secrets.Order().ToList();
    }

    /// <summary>
    /// Parses Build() calls to find sub-build directories.
    /// </summary>
    private IReadOnlyList<string> ParseSubBuilds(string scriptContent)
    {
        var subBuilds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Check for Ando.Build(Directory("path")) pattern
        var andoBuildMatches = AndoBuildPattern().Matches(scriptContent);
        foreach (Match match in andoBuildMatches)
        {
            var path = NormalizePath(match.Groups[1].Value);
            if (!string.IsNullOrEmpty(path))
            {
                subBuilds.Add(path);
            }
        }

        // Also check for legacy Build("path") pattern (backward compatibility)
        var legacyMatches = LegacyBuildPattern().Matches(scriptContent);
        foreach (Match match in legacyMatches)
        {
            var path = NormalizePath(match.Groups[1].Value);
            if (!string.IsNullOrEmpty(path))
            {
                subBuilds.Add(path);
            }
        }

        return subBuilds.ToList();
    }

    /// <summary>
    /// Normalizes a path by removing leading ./ and trailing slashes.
    /// </summary>
    private static string NormalizePath(string path)
    {
        path = path.TrimStart('.').TrimStart('/').TrimStart('\\');
        path = path.TrimEnd('/').TrimEnd('\\');
        return path;
    }
}
