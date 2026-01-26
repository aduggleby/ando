// =============================================================================
// DindChecker.cs
//
// Summary: Pre-flight checker for Docker-in-Docker requirements.
//
// This class runs on the host BEFORE the build container is created to detect
// if any operations in the build script require DIND (Docker-in-Docker) mode.
// If DIND is required, it prompts the user to enable it.
//
// Architecture:
// - Scans registered steps to determine which operations require DIND
// - Checks if --dind flag was provided or ando.config has dind:true
// - If needed, prompts user with options: (Y)es, (a)lways, or Esc
// - "Always" option saves dind:true to ando.config for future runs
//
// Design Decisions:
// - Runs on host before container creation to configure mounts correctly
// - Interactive prompt provides control over Docker socket exposure
// - Config persistence reduces prompting on subsequent runs
// - Follows similar pattern to GitHubScopeChecker for consistency
// =============================================================================

using Ando.Config;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Utilities;

/// <summary>
/// Result of the DIND requirement check.
/// </summary>
public enum DindCheckResult
{
    /// <summary>No operations in the build require DIND.</summary>
    NotRequired,

    /// <summary>DIND enabled via --dind command-line flag.</summary>
    EnabledViaFlag,

    /// <summary>DIND enabled via ando.config setting.</summary>
    EnabledViaConfig,

    /// <summary>User chose (Y)es for this run only.</summary>
    EnabledThisRun,

    /// <summary>User chose (a)lways, saved to ando.config.</summary>
    EnabledAndSaved,

    /// <summary>User pressed Escape to cancel the build.</summary>
    Cancelled
}

/// <summary>
/// Checks if build operations require Docker-in-Docker and prompts user if needed.
/// </summary>
public class DindChecker
{
    private readonly IBuildLogger _logger;

    // Operations that require mounting the Docker socket into the build container.
    // Add new operations here when they need to run Docker commands.
    private static readonly HashSet<string> DindRequiredOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Docker.Build",
        "Docker.Push",
        "GitHub.PushImage"
    };

    public DindChecker(IBuildLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if DIND is required and prompts the user if needed.
    /// </summary>
    /// <param name="registry">The step registry containing registered build steps.</param>
    /// <param name="hasDindFlag">Whether --dind flag was provided on command line.</param>
    /// <param name="projectRoot">The project root directory for config file access.</param>
    /// <returns>The result indicating how DIND should be handled.</returns>
    public DindCheckResult CheckAndPrompt(IStepRegistry registry, bool hasDindFlag, string projectRoot)
    {
        // Determine which DIND operations are in the build.
        var dindOperations = GetDindOperations(registry);

        if (dindOperations.Count == 0)
        {
            return DindCheckResult.NotRequired;
        }

        // Check if --dind flag was provided.
        if (hasDindFlag)
        {
            _logger.Debug("DIND enabled via --dind flag");
            return DindCheckResult.EnabledViaFlag;
        }

        // Check if ando.config has dind:true.
        var config = ProjectConfig.Load(projectRoot);
        if (config.Dind)
        {
            _logger.Debug("DIND enabled via ando.config");
            return DindCheckResult.EnabledViaConfig;
        }

        // Need to prompt the user.
        return PromptForDind(dindOperations, projectRoot);
    }

    /// <summary>
    /// Determines whether DIND should be enabled based on the check result.
    /// </summary>
    /// <param name="result">The result from CheckAndPrompt.</param>
    /// <returns>True if DIND should be enabled, false otherwise.</returns>
    public static bool ShouldEnableDind(DindCheckResult result)
    {
        return result switch
        {
            DindCheckResult.EnabledViaFlag => true,
            DindCheckResult.EnabledViaConfig => true,
            DindCheckResult.EnabledThisRun => true,
            DindCheckResult.EnabledAndSaved => true,
            _ => false
        };
    }

    /// <summary>
    /// Gets the set of operations in the registry that require DIND.
    /// </summary>
    internal static HashSet<string> GetDindOperations(IStepRegistry registry)
    {
        var operations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var step in registry.Steps)
        {
            if (DindRequiredOperations.Contains(step.Name))
            {
                operations.Add(step.Name);
            }
        }

        return operations;
    }

    /// <summary>
    /// Prompts the user to enable DIND mode.
    /// </summary>
    private DindCheckResult PromptForDind(HashSet<string> operations, string projectRoot)
    {
        _logger.Warning("Docker-in-Docker (DIND) mode required.");
        _logger.Warning("");
        _logger.Warning("The following operations require access to the Docker socket:");
        foreach (var op in operations.OrderBy(o => o))
        {
            _logger.Warning($"  - {op}");
        }
        _logger.Warning("");
        _logger.Warning("This will mount /var/run/docker.sock into the build container.");
        _logger.Warning("");

        Console.Write("Enable DIND? (Y)es for this run, (a)lways (save to ando.config), Esc to exit: ");

        var keyInfo = Console.ReadKey(intercept: true);

        // Handle Escape key.
        if (keyInfo.Key == ConsoleKey.Escape)
        {
            Console.WriteLine();
            _logger.Info("Build cancelled.");
            return DindCheckResult.Cancelled;
        }

        // Handle Enter (default to 'Y') or explicit key press.
        var keyChar = keyInfo.Key == ConsoleKey.Enter ? 'y' : char.ToLowerInvariant(keyInfo.KeyChar);
        Console.WriteLine(keyInfo.Key == ConsoleKey.Enter ? "" : keyInfo.KeyChar.ToString());

        switch (keyChar)
        {
            case 'y':
                Console.WriteLine();
                return DindCheckResult.EnabledThisRun;

            case 'a':
                // Save to ando.config.
                var config = new ProjectConfig { Dind = true };
                config.Save(projectRoot);
                _logger.Info("Saved dind:true to ando.config");
                Console.WriteLine();
                return DindCheckResult.EnabledAndSaved;

            default:
                _logger.Info("Build cancelled.");
                return DindCheckResult.Cancelled;
        }
    }
}
