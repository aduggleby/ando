// =============================================================================
// GitHubAuthHelper.cs
//
// Summary: Helper for GitHub authentication across local and CI environments.
//
// GitHubAuthHelper provides a unified way to get GitHub tokens that works both
// locally (extracting from gh CLI config) and in CI (using environment variables).
//
// Authentication flow:
// 1. Check GITHUB_TOKEN environment variable (CI-standard)
// 2. Check GH_TOKEN environment variable (gh CLI alternative)
// 3. Try to extract token from ~/.config/gh/hosts.yml (local development)
//
// Design Decisions:
// - Environment variables take precedence (explicit > implicit)
// - Falls back to gh CLI config for seamless local development
// - Token is passed to containers via GITHUB_TOKEN env var
// - Simple YAML parsing avoids dependency on YAML library
// =============================================================================

using Ando.Logging;

namespace Ando.Utilities;

/// <summary>
/// Helper for obtaining GitHub authentication tokens.
/// Supports both environment variables (CI) and gh CLI config (local development).
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
    /// Gets the GitHub token from environment or gh CLI config.
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
    /// <exception cref="InvalidOperationException">Thrown if no token is found.</exception>
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
    /// Returns the environment dictionary with GITHUB_TOKEN set.
    /// Use this when running commands that need GitHub authentication.
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

    // Resolves the token from various sources.
    private string? ResolveToken()
    {
        // Priority 1: GITHUB_TOKEN environment variable (CI-standard).
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            _logger.Debug("Using GITHUB_TOKEN from environment");
            return token;
        }

        // Priority 2: GH_TOKEN environment variable (gh CLI alternative).
        token = Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrEmpty(token))
        {
            _logger.Debug("Using GH_TOKEN from environment");
            return token;
        }

        // Priority 3: Extract from gh CLI config file.
        token = ExtractFromGhConfig();
        if (!string.IsNullOrEmpty(token))
        {
            _logger.Debug("Using token from gh CLI config");
            return token;
        }

        _logger.Debug("No GitHub token found");
        return null;
    }

    // Extracts the oauth_token from gh CLI config file.
    // Config is at ~/.config/gh/hosts.yml with format:
    // github.com:
    //     oauth_token: gho_xxxxx
    //     user: username
    //     git_protocol: https
    private string? ExtractFromGhConfig()
    {
        try
        {
            var configDir = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

            var hostsPath = Path.Combine(configDir, "gh", "hosts.yml");

            if (!File.Exists(hostsPath))
            {
                return null;
            }

            var content = File.ReadAllText(hostsPath);

            // Simple YAML parsing for oauth_token under github.com.
            // Look for "github.com:" section and then "oauth_token:" line.
            var lines = content.Split('\n');
            var inGitHubSection = false;

            foreach (var line in lines)
            {
                var trimmed = line.TrimEnd();

                // Check for github.com section start.
                if (trimmed.StartsWith("github.com:") || trimmed == "github.com:")
                {
                    inGitHubSection = true;
                    continue;
                }

                // Check for another top-level section (not indented).
                if (inGitHubSection && !string.IsNullOrWhiteSpace(trimmed) && !char.IsWhiteSpace(trimmed[0]))
                {
                    // We've left the github.com section.
                    break;
                }

                // Look for oauth_token in github.com section.
                if (inGitHubSection && trimmed.Contains("oauth_token:"))
                {
                    var tokenStart = trimmed.IndexOf("oauth_token:") + "oauth_token:".Length;
                    var token = trimmed[tokenStart..].Trim();

                    // Remove quotes if present.
                    if ((token.StartsWith('"') && token.EndsWith('"')) ||
                        (token.StartsWith('\'') && token.EndsWith('\'')))
                    {
                        token = token[1..^1];
                    }

                    if (!string.IsNullOrEmpty(token))
                    {
                        return token;
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to read gh config: {ex.Message}");
            return null;
        }
    }
}
