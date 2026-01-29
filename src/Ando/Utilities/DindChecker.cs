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
// - Scans child build scripts (via Ando.Build) recursively for DIND operations
// - Checks if --dind flag was provided or ando.config has dind:true
// - If needed, prompts user with options: (Y)es, (a)lways, or Esc
// - "Always" option saves dind:true to ando.config for future runs
//
// Child Build Scanning:
// - Uses text-based regex scanning to detect Ando.Build(Directory("...")) calls
// - Recursively parses child build.csando files for DIND operations
// - Handles circular references via visited path tracking
// - Gracefully handles missing or unreadable child scripts
//
// Design Decisions:
// - Runs on host before container creation to configure mounts correctly
// - Interactive prompt provides control over Docker socket exposure
// - Config persistence reduces prompting on subsequent runs
// - Follows similar pattern to GitHubScopeChecker for consistency
// - Text-based scanning avoids executing child scripts before DIND is enabled
// =============================================================================

using System.Text.RegularExpressions;
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
    // See CLAUDE.md "DIND Operations Registry" section for documentation.
    private static readonly HashSet<string> DindRequiredOperations = new(StringComparer.OrdinalIgnoreCase)
    {
        "Docker.Build",
        "Docker.Push",
        "Docker.Install",
        "GitHub.PushImage",
        "Playwright.Test"
    };

    // Regex pattern to find Ando.Build calls with Directory paths in script text.
    // Matches: Ando.Build(Directory("./path")) or Ando.Build(Directory("./path"), ...)
    private static readonly Regex AndoBuildPattern = new(
        @"Ando\.Build\s*\(\s*Directory\s*\(\s*""([^""]+)""\s*\)",
        RegexOptions.Compiled);

    // Regex pattern to find DIND-requiring operation calls in script text.
    // Used for scanning child build scripts without executing them.
    private static readonly Regex DindOperationPattern = new(
        @"\b(Docker\.Build|Docker\.Push|Docker\.Install|GitHub\.PushImage|Playwright\.Test)\s*\(",
        RegexOptions.Compiled);

    /// <summary>
    /// Environment variable used to pass DIND setting to child builds.
    /// When set to "1" or "true", child builds automatically enable DIND mode.
    /// </summary>
    public const string DindEnvVar = "ANDO_DIND";

    public DindChecker(IBuildLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Checks if DIND is required and prompts the user if needed.
    /// Also scans child builds for DIND requirements.
    /// </summary>
    /// <param name="registry">The step registry containing registered build steps.</param>
    /// <param name="hasDindFlag">Whether --dind flag was provided on command line.</param>
    /// <param name="projectRoot">The project root directory for config file access.</param>
    /// <returns>The result indicating how DIND should be handled.</returns>
    public DindCheckResult CheckAndPrompt(IStepRegistry registry, bool hasDindFlag, string projectRoot)
    {
        // Determine which DIND operations are in the build (from registered steps).
        var dindOperations = GetDindOperations(registry);

        // Also scan for child builds and check their scripts for DIND operations.
        // This catches DIND requirements in Ando.Build() targets before we start.
        var scriptPath = FindBuildScript(projectRoot);
        if (scriptPath != null)
        {
            var childDindOps = ScanScriptAndChildrenForDind(scriptPath, projectRoot);
            foreach (var op in childDindOps)
            {
                dindOperations.Add(op);
            }
        }

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

        // Check if ANDO_DIND environment variable is set (inherited from parent build).
        var dindEnv = Environment.GetEnvironmentVariable(DindEnvVar);
        if (dindEnv == "1" || string.Equals(dindEnv, "true", StringComparison.OrdinalIgnoreCase))
        {
            _logger.Debug("DIND enabled via ANDO_DIND environment variable (inherited from parent)");
            return DindCheckResult.EnabledViaConfig; // Treat as config-enabled for parent inheritance
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
    /// Finds the build script in the specified directory.
    /// </summary>
    private static string? FindBuildScript(string projectRoot)
    {
        var scriptPath = Path.Combine(projectRoot, "build.csando");
        return File.Exists(scriptPath) ? scriptPath : null;
    }

    /// <summary>
    /// Scans a build script and all its child builds for DIND-requiring operations.
    /// This uses text-based scanning to detect operations without executing the scripts.
    /// </summary>
    /// <param name="scriptPath">Path to the build script to scan.</param>
    /// <param name="projectRoot">Root directory of the project.</param>
    /// <returns>Set of DIND operation names found in the script tree.</returns>
    internal HashSet<string> ScanScriptAndChildrenForDind(string scriptPath, string projectRoot)
    {
        var operations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ScanScriptRecursive(scriptPath, projectRoot, operations, visited);

        return operations;
    }

    /// <summary>
    /// Recursively scans a script and its child builds for DIND operations.
    /// </summary>
    private void ScanScriptRecursive(
        string scriptPath,
        string projectRoot,
        HashSet<string> operations,
        HashSet<string> visited)
    {
        // Avoid infinite loops from circular references.
        var normalizedPath = Path.GetFullPath(scriptPath);
        if (!visited.Add(normalizedPath))
        {
            return;
        }

        // Read the script file directly without pre-checking existence.
        // This avoids a TOCTOU race condition where the file could be deleted
        // between the existence check and the read operation.
        string scriptContent;
        try
        {
            scriptContent = File.ReadAllText(scriptPath);
        }
        catch (FileNotFoundException)
        {
            _logger.Debug($"Child build script not found: {scriptPath}");
            return;
        }
        catch (Exception ex)
        {
            _logger.Debug($"Could not read script {scriptPath}: {ex.Message}");
            return;
        }

        // Find DIND operations in this script.
        var dindMatches = DindOperationPattern.Matches(scriptContent);
        foreach (Match match in dindMatches)
        {
            var operationName = match.Groups[1].Value;
            if (operations.Add(operationName))
            {
                _logger.Debug($"Found DIND operation in {Path.GetFileName(scriptPath)}: {operationName}");
            }
        }

        // Find child builds and scan them recursively.
        var childBuildMatches = AndoBuildPattern.Matches(scriptContent);
        foreach (Match match in childBuildMatches)
        {
            var relativePath = match.Groups[1].Value;
            var scriptDir = Path.GetDirectoryName(scriptPath) ?? projectRoot;

            // Resolve the child build path.
            var childDir = Path.GetFullPath(Path.Combine(scriptDir, relativePath));

            // Check if it's a .csando file or a directory.
            string childScriptPath;
            if (relativePath.EndsWith(".csando", StringComparison.OrdinalIgnoreCase))
            {
                childScriptPath = childDir;
            }
            else
            {
                childScriptPath = Path.Combine(childDir, "build.csando");
            }

            _logger.Debug($"Scanning child build: {childScriptPath}");
            ScanScriptRecursive(childScriptPath, projectRoot, operations, visited);
        }
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
                // Save to ando.config (preserving existing settings).
                var existingConfig = ProjectConfig.Load(projectRoot);
                var newConfig = existingConfig with { Dind = true };
                newConfig.Save(projectRoot);
                _logger.Info("Saved dind:true to ando.config");
                Console.WriteLine();
                return DindCheckResult.EnabledAndSaved;

            default:
                _logger.Info("Build cancelled.");
                return DindCheckResult.Cancelled;
        }
    }
}
