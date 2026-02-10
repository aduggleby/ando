// =============================================================================
// GitHubService.cs
//
// Summary: Implementation of GitHub API operations.
//
// Handles all interactions with the GitHub API including authentication,
// commit status updates, and repository operations. Uses GitHub App
// installation tokens for authenticated API access.
//
// Design Decisions:
// - Caches installation tokens for 50 minutes (they're valid for 60)
// - Uses git CLI for clone/fetch operations (simpler than API)
// - Authenticates clone URLs with installation token
// =============================================================================

using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text.Json;
using Ando.Server.Configuration;
using Microsoft.Extensions.Options;

namespace Ando.Server.GitHub;

/// <summary>
/// Implementation of GitHub API operations.
/// </summary>
public class GitHubService : IGitHubService
{
    private readonly HttpClient _httpClient;
    private readonly GitHubAppAuthenticator _authenticator;
    private readonly GitHubSettings _settings;
    private readonly ILogger<GitHubService> _logger;

    // Cache installation tokens (they're valid for 1 hour, we cache for 50 min)
    private readonly ConcurrentDictionary<long, (string Token, DateTime ExpiresAt)> _tokenCache = new();
    private string? _appSlugCache;

    public GitHubService(
        IHttpClientFactory httpClientFactory,
        GitHubAppAuthenticator authenticator,
        IOptions<GitHubSettings> settings,
        ILogger<GitHubService> logger)
    {
        _httpClient = httpClientFactory.CreateClient("GitHub");
        _authenticator = authenticator;
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetAppSlugAsync()
    {
        if (!string.IsNullOrWhiteSpace(_appSlugCache))
        {
            return _appSlugCache;
        }

        try
        {
            var jwt = _authenticator.GenerateJwt();

            // https://docs.github.com/en/rest/apps/apps#get-the-authenticated-app
            var request = new HttpRequestMessage(HttpMethod.Get, "app");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("Failed to get GitHub app info: {Status} - {Error}", response.StatusCode, error);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            // GitHub returns a "slug" field for apps; fall back to "name" if needed.
            if (doc.RootElement.TryGetProperty("slug", out var slugProp))
            {
                var slug = slugProp.GetString();
                if (!string.IsNullOrWhiteSpace(slug))
                {
                    _appSlugCache = slug;
                    return slug;
                }
            }

            if (doc.RootElement.TryGetProperty("name", out var nameProp))
            {
                var name = nameProp.GetString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    // Not ideal (name may contain spaces), but better than nothing.
                    _appSlugCache = name;
                    return name;
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting GitHub app slug");
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> SetCommitStatusAsync(
        long installationId,
        string repoFullName,
        string commitSha,
        CommitStatusState state,
        string? description = null,
        string? targetUrl = null,
        string context = "ci/ando")
    {
        try
        {
            var token = await GetInstallationTokenAsync(installationId);
            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Failed to get installation token for {InstallationId}", installationId);
                return false;
            }

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"repos/{repoFullName}/statuses/{commitSha}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var body = new
            {
                state = state.ToString().ToLowerInvariant(),
                description = description ?? GetDefaultDescription(state),
                target_url = targetUrl,
                context
            };

            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                System.Text.Encoding.UTF8,
                "application/json");

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Failed to set commit status for {Repo}/{Sha}: {Status} - {Error}",
                    repoFullName, commitSha[..8], response.StatusCode, error);
                return false;
            }

            _logger.LogInformation(
                "Set commit status {State} for {Repo}/{Sha}",
                state, repoFullName, commitSha[..8]);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting commit status for {Repo}/{Sha}", repoFullName, commitSha[..8]);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> CloneRepositoryAsync(
        long installationId,
        string repoFullName,
        string branch,
        string commitSha,
        string targetDirectory,
        string? gitTokenOverride = null)
    {
        try
        {
            var token = !string.IsNullOrWhiteSpace(gitTokenOverride)
                ? gitTokenOverride
                : await GetInstallationTokenAsync(installationId);
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            // Create authenticated clone URL
            var cloneUrl = $"https://x-access-token:{token}@github.com/{repoFullName}.git";

            // Clone the repository
            var cloneResult = await RunGitCommandAsync(
                null,
                "clone",
                "--branch", branch,
                "--single-branch",
                "--depth", "50",  // Shallow clone for speed
                cloneUrl,
                targetDirectory);

            if (!cloneResult)
            {
                return false;
            }

            // Ensure the stored git remote URL does not embed credentials.
            // Build scripts may call `git remote get-url origin` and log it; never leak tokens to logs.
            var cleanRemoteUrl = $"https://github.com/{repoFullName}.git";
            await RunGitCommandAsync(targetDirectory, "remote", "set-url", "origin", cleanRemoteUrl);

            // Checkout the specific commit *on the branch* so the working copy is not left in
            // detached-HEAD state. This matters for publish profiles that include Git.Push.
            return await RunGitCommandAsync(targetDirectory, "checkout", "-B", branch, commitSha);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cloning repository {Repo}", repoFullName);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<bool> FetchAndCheckoutAsync(
        long installationId,
        string repoFullName,
        string branch,
        string commitSha,
        string repoDirectory,
        string? gitTokenOverride = null)
    {
        try
        {
            var token = !string.IsNullOrWhiteSpace(gitTokenOverride)
                ? gitTokenOverride
                : await GetInstallationTokenAsync(installationId);
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            // Update remote URL with fresh token
            var remoteUrl = $"https://x-access-token:{token}@github.com/{repoFullName}.git";
            await RunGitCommandAsync(repoDirectory, "remote", "set-url", "origin", remoteUrl);

            // Fetch latest
            var fetchResult = await RunGitCommandAsync(repoDirectory, "fetch", "origin", branch);
            if (!fetchResult)
            {
                return false;
            }

            // Restore a clean remote URL (never persist credentials in the repo config).
            var cleanRemoteUrl = $"https://github.com/{repoFullName}.git";
            await RunGitCommandAsync(repoDirectory, "remote", "set-url", "origin", cleanRemoteUrl);

            // Checkout the specific commit *on the branch* so the working copy is not left in
            // detached-HEAD state. This matters for publish profiles that include Git.Push.
            return await RunGitCommandAsync(repoDirectory, "checkout", "-B", branch, commitSha);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching repository {Repo}", repoFullName);
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetInstallationTokenAsync(long installationId)
    {
        // Check cache first
        if (_tokenCache.TryGetValue(installationId, out var cached) && cached.ExpiresAt > DateTime.UtcNow)
        {
            return cached.Token;
        }

        try
        {
            var jwt = _authenticator.GenerateJwt();

            var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"app/installations/{installationId}/access_tokens");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning(
                    "Failed to get installation token for {InstallationId}: {Status} - {Error}",
                    installationId, response.StatusCode, error);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var token = doc.RootElement.GetProperty("token").GetString();

            if (!string.IsNullOrEmpty(token))
            {
                // Cache for 50 minutes (tokens are valid for 60)
                _tokenCache[installationId] = (token, DateTime.UtcNow.AddMinutes(50));
            }

            return token;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting installation token for {InstallationId}", installationId);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubRepository>> GetInstallationRepositoriesAsync(long installationId)
    {
        var token = await GetInstallationTokenAsync(installationId);
        if (string.IsNullOrEmpty(token))
        {
            return [];
        }

        return await GetRepositoriesAsync(token, "installation/repositories");
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<GitHubRepository>> GetUserRepositoriesAsync(string accessToken)
    {
        return await GetRepositoriesAsync(accessToken, "user/repos?per_page=100&affiliation=owner");
    }

    /// <inheritdoc />
    public async Task<(long InstallationId, GitHubRepository Repository)?> GetRepositoryInstallationAsync(string repoFullName)
    {
        try
        {
            var jwt = _authenticator.GenerateJwt();

            // First, get the installation for this repo
            var installationRequest = new HttpRequestMessage(HttpMethod.Get, $"repos/{repoFullName}/installation");
            installationRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

            var installationResponse = await _httpClient.SendAsync(installationRequest);

            if (!installationResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "No GitHub App installation found for {Repo}: {Status}",
                    repoFullName, installationResponse.StatusCode);
                return null;
            }

            var installationContent = await installationResponse.Content.ReadAsStringAsync();
            using var installationDoc = JsonDocument.Parse(installationContent);
            var installationId = installationDoc.RootElement.GetProperty("id").GetInt64();

            // Now get the repo info using the installation token
            var token = await GetInstallationTokenAsync(installationId);
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var repoRequest = new HttpRequestMessage(HttpMethod.Get, $"repos/{repoFullName}");
            repoRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var repoResponse = await _httpClient.SendAsync(repoRequest);

            if (!repoResponse.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get repository info for {Repo}: {Status}", repoFullName, repoResponse.StatusCode);
                return null;
            }

            var repoContent = await repoResponse.Content.ReadAsStringAsync();
            using var repoDoc = JsonDocument.Parse(repoContent);
            var repo = repoDoc.RootElement;
            var owner = repo.GetProperty("owner");

            var repository = new GitHubRepository(
                Id: repo.GetProperty("id").GetInt64(),
                FullName: repo.GetProperty("full_name").GetString() ?? "",
                HtmlUrl: repo.GetProperty("html_url").GetString() ?? "",
                CloneUrl: repo.GetProperty("clone_url").GetString() ?? "",
                DefaultBranch: repo.TryGetProperty("default_branch", out var db) ? db.GetString() ?? "main" : "main",
                IsPrivate: repo.TryGetProperty("private", out var priv) && priv.GetBoolean(),
                OwnerId: owner.GetProperty("id").GetInt64(),
                OwnerLogin: owner.GetProperty("login").GetString() ?? ""
            );

            return (installationId, repository);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting repository installation for {Repo}", repoFullName);
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<string?> GetBranchHeadShaAsync(long installationId, string repoFullName, string branch)
    {
        try
        {
            var token = await GetInstallationTokenAsync(installationId);
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            var request = new HttpRequestMessage(HttpMethod.Get, $"repos/{repoFullName}/branches/{branch}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get branch {Branch} for {Repo}: {Status}", branch, repoFullName, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            return doc.RootElement.GetProperty("commit").GetProperty("sha").GetString();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting branch head SHA for {Repo}/{Branch}", repoFullName, branch);
            return null;
        }
    }

    /// <summary>
    /// Gets repositories from a GitHub API endpoint.
    /// </summary>
    private async Task<IReadOnlyList<GitHubRepository>> GetRepositoriesAsync(string token, string endpoint)
    {
        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get repositories from {Endpoint}: {Status}", endpoint, response.StatusCode);
                return [];
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            var repos = new List<GitHubRepository>();

            // Handle both direct array and { repositories: [...] } responses
            var repoArray = doc.RootElement.ValueKind == JsonValueKind.Array
                ? doc.RootElement
                : doc.RootElement.GetProperty("repositories");

            foreach (var repo in repoArray.EnumerateArray())
            {
                var owner = repo.GetProperty("owner");
                repos.Add(new GitHubRepository(
                    Id: repo.GetProperty("id").GetInt64(),
                    FullName: repo.GetProperty("full_name").GetString() ?? "",
                    HtmlUrl: repo.GetProperty("html_url").GetString() ?? "",
                    CloneUrl: repo.GetProperty("clone_url").GetString() ?? "",
                    DefaultBranch: repo.TryGetProperty("default_branch", out var db) ? db.GetString() ?? "main" : "main",
                    IsPrivate: repo.TryGetProperty("private", out var priv) && priv.GetBoolean(),
                    OwnerId: owner.GetProperty("id").GetInt64(),
                    OwnerLogin: owner.GetProperty("login").GetString() ?? ""
                ));
            }

            return repos;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting repositories from {Endpoint}", endpoint);
            return [];
        }
    }

    /// <summary>
    /// Runs a git command and returns success status.
    /// </summary>
    private async Task<bool> RunGitCommandAsync(string? workingDirectory, params string[] args)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            foreach (var arg in args)
            {
                startInfo.ArgumentList.Add(arg);
            }

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start git process");
                return false;
            }

            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                _logger.LogWarning("Git command failed: git {Args} - {Error}",
                    string.Join(" ", args.Select(SanitizeArg)), error);
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running git command: {Args}", string.Join(" ", args.Select(SanitizeArg)));
            return false;
        }
    }

    /// <summary>
    /// Sanitizes git arguments for logging (removes tokens).
    /// </summary>
    private static string SanitizeArg(string arg)
    {
        if (arg.Contains("x-access-token:"))
        {
            return "[REDACTED_URL]";
        }
        return arg;
    }

    /// <summary>
    /// Gets default description for a commit status state.
    /// </summary>
    private static string GetDefaultDescription(CommitStatusState state) => state switch
    {
        CommitStatusState.Pending => "Build is queued",
        CommitStatusState.Success => "Build succeeded",
        CommitStatusState.Failure => "Build failed",
        CommitStatusState.Error => "Build error",
        _ => "Unknown status"
    };

    /// <inheritdoc />
    public async Task<string?> GetFileContentAsync(long installationId, string repoFullName, string filePath, string? branch = null)
    {
        try
        {
            var token = await GetInstallationTokenAsync(installationId);
            if (string.IsNullOrEmpty(token))
            {
                return null;
            }

            // Build the endpoint URL
            var endpoint = $"repos/{repoFullName}/contents/{filePath}";
            if (!string.IsNullOrEmpty(branch))
            {
                endpoint += $"?ref={Uri.EscapeDataString(branch)}";
            }

            var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                {
                    _logger.LogDebug("File not found: {Repo}/{Path}", repoFullName, filePath);
                }
                else
                {
                    _logger.LogWarning("Failed to get file content for {Repo}/{Path}: {Status}", repoFullName, filePath, response.StatusCode);
                }
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(content);

            // GitHub returns base64-encoded content
            var encodedContent = doc.RootElement.GetProperty("content").GetString();
            if (string.IsNullOrEmpty(encodedContent))
            {
                return null;
            }

            // Decode base64 (GitHub includes newlines in the encoding)
            var base64Clean = encodedContent.Replace("\n", "").Replace("\r", "");
            var bytes = Convert.FromBase64String(base64Clean);
            return System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting file content for {Repo}/{Path}", repoFullName, filePath);
            return null;
        }
    }
}
