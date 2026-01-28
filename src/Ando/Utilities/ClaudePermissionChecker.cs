// =============================================================================
// ClaudePermissionChecker.cs
//
// Summary: Prompts user to confirm Claude CLI usage with elevated permissions.
//
// Several ANDO commands (commit, bump, docs) use Claude CLI to generate content.
// These commands run Claude with --dangerously-skip-permissions to allow file
// edits without interactive prompts. This checker ensures users understand and
// consent to this behavior.
//
// Architecture:
// - Checks if allowClaude is set in ando.config
// - If not, prompts user with options: (Y)es, (n)o, (a)lways
// - "Always" saves allowClaude:true to ando.config
// - Follows the same pattern as DindChecker for consistency
//
// Design Decisions:
// - Runs before any Claude CLI invocation
// - Explicit consent required for security-sensitive operations
// - Config persistence reduces prompting on subsequent runs
// =============================================================================

using Ando.Config;
using Ando.Logging;

namespace Ando.Utilities;

/// <summary>
/// Result of the Claude permission check.
/// </summary>
public enum ClaudePermissionResult
{
    /// <summary>User chose (Y)es for this run only.</summary>
    AllowedThisRun,

    /// <summary>User chose (a)lways, saved to ando.config.</summary>
    AllowedAndSaved,

    /// <summary>Permission already granted via ando.config.</summary>
    AllowedViaConfig,

    /// <summary>User chose (n)o or pressed Escape.</summary>
    Denied
}

/// <summary>
/// Checks and prompts for permission to run Claude CLI with elevated permissions.
/// </summary>
public class ClaudePermissionChecker
{
    private readonly IBuildLogger _logger;

    public ClaudePermissionChecker(IBuildLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if Claude permission is granted and prompts the user if needed.
    /// </summary>
    /// <param name="projectRoot">The project root directory for config file access.</param>
    /// <param name="commandName">The command requesting Claude access (for display).</param>
    /// <returns>The result indicating whether Claude can be used.</returns>
    public ClaudePermissionResult CheckAndPrompt(string projectRoot, string commandName)
    {
        // Check if ando.config has allowClaude:true.
        var config = ProjectConfig.Load(projectRoot);
        if (config.AllowClaude)
        {
            _logger.Debug("Claude permission granted via ando.config");
            return ClaudePermissionResult.AllowedViaConfig;
        }

        // Need to prompt the user.
        return PromptForPermission(commandName, projectRoot);
    }

    /// <summary>
    /// Determines whether Claude can be used based on the check result.
    /// </summary>
    /// <param name="result">The result from CheckAndPrompt.</param>
    /// <returns>True if Claude can be used, false otherwise.</returns>
    public static bool IsAllowed(ClaudePermissionResult result)
    {
        return result switch
        {
            ClaudePermissionResult.AllowedThisRun => true,
            ClaudePermissionResult.AllowedAndSaved => true,
            ClaudePermissionResult.AllowedViaConfig => true,
            _ => false
        };
    }

    /// <summary>
    /// Prompts the user to allow Claude CLI usage.
    /// </summary>
    private ClaudePermissionResult PromptForPermission(string commandName, string projectRoot)
    {
        Console.WriteLine();
        _logger.Warning($"The '{commandName}' command uses Claude CLI for AI-powered features.");
        _logger.Warning("");
        _logger.Warning("Claude will run with --dangerously-skip-permissions, which allows it to:");
        _logger.Warning("  - Read files in your project");
        _logger.Warning("  - Edit and create files");
        _logger.Warning("  - Execute commands");
        _logger.Warning("");

        Console.Write("Allow Claude? (Y)es for this run, (n)o, (a)lways (save to ando.config): ");

        var keyInfo = Console.ReadKey(intercept: true);

        // Handle Escape key.
        if (keyInfo.Key == ConsoleKey.Escape)
        {
            Console.WriteLine();
            _logger.Info("Command cancelled.");
            return ClaudePermissionResult.Denied;
        }

        // Handle Enter (default to 'Y') or explicit key press.
        var keyChar = keyInfo.Key == ConsoleKey.Enter ? 'y' : char.ToLowerInvariant(keyInfo.KeyChar);
        Console.WriteLine(keyInfo.Key == ConsoleKey.Enter ? "" : keyInfo.KeyChar.ToString());

        switch (keyChar)
        {
            case 'y':
                Console.WriteLine();
                return ClaudePermissionResult.AllowedThisRun;

            case 'a':
                // Save to ando.config.
                var existingConfig = ProjectConfig.Load(projectRoot);
                var newConfig = existingConfig with { AllowClaude = true };
                newConfig.Save(projectRoot);
                _logger.Info("Saved allowClaude:true to ando.config");
                Console.WriteLine();
                return ClaudePermissionResult.AllowedAndSaved;

            default:
                Console.WriteLine();
                _logger.Info("Command cancelled.");
                return ClaudePermissionResult.Denied;
        }
    }
}
