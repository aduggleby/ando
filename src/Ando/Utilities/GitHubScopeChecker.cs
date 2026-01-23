// =============================================================================
// GitHubScopeChecker.cs
//
// Summary: Pre-flight checker for GitHub OAuth scopes.
//
// This class runs on the host BEFORE the build container is created to verify
// that the GitHub token has the required scopes for operations in the build
// script. If scopes are missing, it prompts the user to re-authenticate.
//
// Architecture:
// - Scans registered steps to determine which GitHub operations will run
// - Maps operations to required scopes (e.g., GitHub.PushImage -> write:packages)
// - Uses `gh auth status` to check current scopes
// - Prompts for re-authentication via `gh auth refresh` if needed
//
// Design Decisions:
// - Runs on host to access gh CLI (not available in build container)
// - Checks scopes BEFORE container creation to fail fast
// - Interactive prompt allows user to fix auth without restarting build
// - Non-blocking for operations that don't need special scopes
// =============================================================================

using Ando.Logging;
using Ando.Steps;

namespace Ando.Utilities;

/// <summary>
/// Checks and ensures required GitHub OAuth scopes are available before build execution.
/// </summary>
public class GitHubScopeChecker
{
    private readonly IBuildLogger _logger;

    // Maps GitHub operation step names to their required OAuth scopes.
    private static readonly Dictionary<string, string[]> OperationScopes = new()
    {
        ["GitHub.PushImage"] = ["write:packages"],
        // Add more mappings as needed:
        // ["GitHub.CreateRelease"] = ["repo"],  // repo scope is usually default
    };

    public GitHubScopeChecker(IBuildLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if the registered steps require GitHub scopes and verifies they're available.
    /// If scopes are missing, prompts the user to re-authenticate.
    /// </summary>
    /// <param name="registry">The step registry containing registered build steps.</param>
    /// <returns>True if all required scopes are available, false if user declined to authenticate.</returns>
    public async Task<bool> EnsureRequiredScopesAsync(IStepRegistry registry)
    {
        // Determine which scopes are needed based on registered steps.
        var requiredScopes = GetRequiredScopes(registry);
        if (requiredScopes.Count == 0)
        {
            return true; // No special scopes needed
        }

        // Get current scopes from gh CLI.
        var currentScopes = GetCurrentScopes();

        // Find missing scopes.
        var missingScopes = requiredScopes.Except(currentScopes, StringComparer.OrdinalIgnoreCase).ToList();
        if (missingScopes.Count == 0)
        {
            _logger.Debug($"GitHub scopes verified: {string.Join(", ", requiredScopes)}");

            // Update GITHUB_TOKEN with fresh token from keyring (in case env var is stale).
            var freshToken = GetNewToken();
            if (!string.IsNullOrEmpty(freshToken))
            {
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", freshToken);
                _logger.Debug("Updated GITHUB_TOKEN with fresh token from keyring");
            }

            return true; // All scopes available
        }

        // Prompt user to re-authenticate.
        _logger.Warning($"GitHub authentication missing required scope(s): {string.Join(", ", missingScopes)}");
        _logger.Warning("");
        _logger.Warning("This build requires additional GitHub permissions.");
        _logger.Warning("");

        Console.Write($"Re-authenticate now to add missing scopes? [Y/n] ");
        var response = Console.ReadLine()?.Trim().ToLowerInvariant();

        if (response == "n" || response == "no")
        {
            _logger.Error("Build requires GitHub scopes that are not available.");
            _logger.Error($"Re-authenticate manually with: gh auth refresh --hostname github.com --scopes {string.Join(",", missingScopes)}");
            return false;
        }

        // Run gh auth refresh to add scopes.
        Console.WriteLine();
        var success = await RefreshAuthWithScopesAsync(missingScopes);

        if (!success)
        {
            _logger.Error("Failed to refresh GitHub authentication.");
            return false;
        }

        _logger.Info("GitHub authentication updated successfully.");
        Console.WriteLine();
        return true;
    }

    /// <summary>
    /// Determines which GitHub OAuth scopes are required based on registered steps.
    /// </summary>
    private HashSet<string> GetRequiredScopes(IStepRegistry registry)
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in registry.Steps)
        {
            if (OperationScopes.TryGetValue(step.Name, out var stepScopes))
            {
                foreach (var scope in stepScopes)
                {
                    scopes.Add(scope);
                }
            }
        }

        return scopes;
    }

    /// <summary>
    /// Gets the current OAuth scopes from gh CLI.
    /// Runs with GITHUB_TOKEN unset to check the keyring token's scopes.
    /// </summary>
    private HashSet<string> GetCurrentScopes()
    {
        var scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            // Use bash to unset GITHUB_TOKEN so we check the keyring token's scopes.
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"unset GITHUB_TOKEN; gh auth status 2>&1\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return scopes;

            // Output goes to stdout due to 2>&1 redirect
            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);

            // Parse "Token scopes: 'scope1', 'scope2', ..." line
            foreach (var line in output.Split('\n'))
            {
                if (line.Contains("Token scopes:"))
                {
                    var scopesPart = line[(line.IndexOf("Token scopes:") + "Token scopes:".Length)..];
                    foreach (var scope in scopesPart.Split(','))
                    {
                        var cleaned = scope.Trim().Trim('\'', '"', ' ');
                        if (!string.IsNullOrEmpty(cleaned))
                        {
                            scopes.Add(cleaned);
                        }
                    }
                    break;
                }
            }

            _logger.Debug($"Current GitHub token scopes: {string.Join(", ", scopes)}");
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to get GitHub scopes: {ex.Message}");
        }

        return scopes;
    }

    /// <summary>
    /// Runs gh auth refresh to add missing scopes.
    /// </summary>
    private async Task<bool> RefreshAuthWithScopesAsync(List<string> scopes)
    {
        try
        {
            // Save and temporarily unset GITHUB_TOKEN, as gh auth refresh won't work
            // when the token is set via environment variable.
            var savedToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN");

            if (!string.IsNullOrEmpty(savedToken))
            {
                _logger.Debug("Temporarily unsetting GITHUB_TOKEN for auth refresh");
                Environment.SetEnvironmentVariable("GITHUB_TOKEN", null);
            }

            var scopesArg = string.Join(",", scopes);
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "gh",
                Arguments = $"auth refresh --hostname github.com --scopes {scopesArg}",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null)
            {
                RestoreToken(savedToken);
                return false;
            }

            await process.WaitForExitAsync();
            var success = process.ExitCode == 0;

            if (success)
            {
                // Get the new token with updated scopes.
                var newToken = GetNewToken();
                if (!string.IsNullOrEmpty(newToken))
                {
                    Environment.SetEnvironmentVariable("GITHUB_TOKEN", newToken);
                    _logger.Debug("Updated GITHUB_TOKEN with refreshed token");
                }
            }
            else
            {
                // Restore original token if refresh failed.
                RestoreToken(savedToken);
            }

            return success;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Failed to refresh GitHub auth: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Restores saved GITHUB_TOKEN environment variable.
    /// </summary>
    private static void RestoreToken(string? savedToken)
    {
        if (!string.IsNullOrEmpty(savedToken))
        {
            Environment.SetEnvironmentVariable("GITHUB_TOKEN", savedToken);
        }
    }

    /// <summary>
    /// Gets a fresh token from gh auth token (from keyring, not env var).
    /// </summary>
    private string? GetNewToken()
    {
        try
        {
            // Use bash to unset GITHUB_TOKEN so we get the keyring token
            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "bash",
                Arguments = "-c \"unset GITHUB_TOKEN; gh auth token\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(startInfo);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit(5000);

            return process.ExitCode == 0 ? output : null;
        }
        catch
        {
            return null;
        }
    }
}
