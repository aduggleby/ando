// =============================================================================
// GitHubAuthHelper.cs
//
// Summary: Helper for GitHub authentication.
//
// Authentication sources (in order):
// 1. GITHUB_TOKEN environment variable (for CI or explicit configuration)
// 2. `gh auth token` command (for local development with gh CLI)
//
// Design Decisions:
// - Keep it simple: only two sources, no legacy config file parsing
// - Environment variable takes precedence (explicit > implicit)
// - Token is passed to containers via GITHUB_TOKEN env var
// =============================================================================

using Ando.Logging;

namespace Ando.Utilities;

/// <summary>
/// Helper for obtaining GitHub authentication tokens.
/// </summary>
public class GitHubAuthHelper
{
    private readonly IBuildLogger _logger;
    private string? _cachedToken;
    private bool _tokenResolved;

    public GitHubAuthHelper(IBuildLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Gets the GitHub token from GITHUB_TOKEN env var or gh CLI.
    /// Returns null if no token is available.
    /// </summary>
    public string? GetToken()
    {
        if (_tokenResolved)
        {
            return _cachedToken;
        }

        _cachedToken = ResolveToken();
        _tokenResolved = true;
        return _cachedToken;
    }

    /// <summary>
    /// Gets the GitHub token, throwing if not available.
    /// </summary>
    public string GetRequiredToken()
    {
        var token = GetToken();
        if (string.IsNullOrEmpty(token))
        {
            throw new InvalidOperationException(
                "GitHub token not found. Either:\n" +
                "  1. Set GITHUB_TOKEN environment variable\n" +
                "  2. Run 'gh auth login' to authenticate the GitHub CLI");
        }
        return token;
    }

    /// <summary>
    /// Returns environment dictionary with GITHUB_TOKEN set.
    /// </summary>
    public Dictionary<string, string> GetEnvironment()
    {
        var env = new Dictionary<string, string>();
        var token = GetToken();
        if (!string.IsNullOrEmpty(token))
        {
            env["GITHUB_TOKEN"] = token;
        }
        return env;
    }

    /// <summary>
    /// Resolves the token: GITHUB_TOKEN env var first, then gh auth token.
    /// </summary>
    private string? ResolveToken()
    {
        // Priority 1: GITHUB_TOKEN environment variable.
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            _logger.Debug("Using GITHUB_TOKEN from environment");
            return token;
        }

        // Priority 2: gh auth token command.
        token = RunGhAuthToken();
        if (!string.IsNullOrEmpty(token))
        {
            _logger.Debug("Using token from gh auth token");
            return token;
        }

        _logger.Debug("No GitHub token found");
        return null;
    }

    /// <summary>
    /// Gets token using `gh auth token` command.
    /// </summary>
    private string? RunGhAuthToken()
    {
        try
        {
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "gh",
                Arguments = "auth token",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return process.ExitCode == 0 && !string.IsNullOrEmpty(output) ? output : null;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to run gh auth token: {ex.Message}");
            return null;
        }
    }
}
