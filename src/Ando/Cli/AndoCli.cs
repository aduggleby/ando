// =============================================================================
// AndoCli.cs
//
// Summary: Main CLI handler for the ANDO build system.
//
// This class orchestrates all CLI functionality including command parsing,
// build execution, Docker container management, and cleanup operations.
// It serves as the primary entry point for user interaction with ANDO.
//
// Architecture:
// - Parses command-line arguments to determine which command to execute
// - Manages the lifecycle of builds (initialization, execution, cleanup)
// - All builds run in Docker containers for reproducibility
// - Provides helpful error messages and usage information
//
// Design Decisions:
// - Implements IDisposable to ensure log file handles are properly released
// - Uses warm containers by default for faster subsequent builds
// - Docker is mandatory - no local execution mode for consistency
// - Exit codes follow Unix conventions (0=success, non-zero=specific errors)
//
// Exit Codes:
// - 0: Success
// - 1: Build workflow failed
// - 2: Script not found or invalid file
// - 3: Docker not available
// - 4: Profile validation failed
// - 5: Generic error (Roslyn errors, exceptions, etc.)
// - 6: .env.ando not gitignored (user declined to continue)
// - 7: GitHub OAuth scope check failed (user declined to re-authenticate)
// - 8: DIND check cancelled (user pressed Escape)
// =============================================================================

using System.Reflection;
using System.Security.Cryptography;
using Ando.Cli.Commands;
using Ando.Config;
using Ando.Execution;
using Ando.Logging;
using Ando.Profiles;
using Ando.Scripting;
using Ando.Utilities;
using Ando.Workflow;

namespace Ando.Cli;

/// <summary>
/// Main command-line interface handler for ANDO build system.
/// Parses arguments, manages execution context, and runs build workflows.
/// </summary>
public class AndoCli : IDisposable
{
    private readonly string[] _args;
    private readonly ConsoleLogger _logger;

    // Extracts version from assembly metadata for display in header.
    // Falls back to "0.0.0" if version is not embedded (e.g., during development).
    private static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

    /// <summary>
    /// Initializes the CLI with command-line arguments.
    /// </summary>
    /// <param name="args">Command-line arguments passed to the application.</param>
    public AndoCli(string[] args)
    {
        _args = args;

        // Log file is created in the current directory alongside the build script.
        // This keeps logs associated with the project being built.
        var logPath = Path.Combine(Environment.CurrentDirectory, "build.csando.log");

        // Color detection happens early so error messages can be properly formatted.
        // Respects both --no-color flag and NO_COLOR environment variable.
        _logger = new ConsoleLogger(useColor: !IsNoColorRequested(args), logFilePath: logPath);
    }

    /// <summary>
    /// Disposes resources, specifically closing the log file handle.
    /// </summary>
    public void Dispose()
    {
        _logger.Dispose();
    }

    // Displays the ANDO ASCII art logo and version.
    // Uses ANSI escape codes for color when supported.
    // For nested builds, shows a compact header instead.
    private void PrintHeader()
    {
        var useColor = !IsNoColorRequested(_args);
        var cyan = useColor ? "\u001b[1;36m" : "";
        var gray = useColor ? "\u001b[2;37m" : "";
        var reset = useColor ? "\u001b[0m" : "";

        // Check if this is a nested build.
        var indentStr = Environment.GetEnvironmentVariable(ConsoleLogger.IndentLevelEnvVar);
        var indentLevel = int.TryParse(indentStr, out var level) ? level : 0;

        if (indentLevel > 0)
        {
            // Nested build: print compact header with indent.
            var indent = string.Concat(Enumerable.Repeat($"{gray}│{reset}  ", indentLevel));
            Console.WriteLine();
            Console.WriteLine($"{indent}{cyan}▶  ANDO{reset} {gray}(nested build){reset}");
        }
        else
        {
            // Top-level build: print full ASCII art header.
            Console.WriteLine();
            Console.WriteLine($"{cyan}   _   _  _ ___   ___  {reset}");
            Console.WriteLine($"{cyan}  /_\\ | \\| |   \\ / _ \\ {reset}");
            Console.WriteLine($"{cyan} / _ \\| .` | |) | (_) |{reset}");
            Console.WriteLine($"{cyan}/_/ \\_\\_|\\_|___/ \\___/ {reset}{gray}v{Version}{reset}");
            Console.WriteLine();
            Console.WriteLine($"Minimal & Fast Build System");
        }
        Console.WriteLine();
    }

    /// <summary>
    /// Main entry point that routes to the appropriate command handler.
    /// </summary>
    /// <returns>Exit code: 0 for success, non-zero for various failure modes.</returns>
    public async Task<int> RunAsync()
    {
        // Command routing logic:
        // - No args or "run" or any non-flag argument -> run the build
        // - "help", "--help", "-h" -> show help
        // - "clean" -> cleanup command
        // - "commit" -> AI-generated commit
        // - "bump" -> Version bumping
        // - "docs" -> Documentation update
        // - "release" -> Full release workflow
        // This allows "ando" and "ando run" to behave identically.
        // Also treat options (starting with -) as implicit run command, except -v/-h/--version/--help.
        var isRunOption = _args.Length > 0 && _args[0].StartsWith("-") &&
                          _args[0] != "-v" && _args[0] != "--version" &&
                          _args[0] != "-h" && _args[0] != "--help";
        if (_args.Length == 0 || _args[0] == "run" || isRunOption || !_args[0].StartsWith("-") && _args[0] != "clean" && _args[0] != "help" && _args[0] != "verify" && _args[0] != "commit" && _args[0] != "bump" && _args[0] != "docs" && _args[0] != "release")
        {
            PrintHeader();
            return await RunCommandAsync();
        }

        if (_args[0] == "help" || _args[0] == "--help" || _args[0] == "-h")
        {
            PrintHeader();
            return HelpCommand();
        }

        if (_args[0] == "verify")
        {
            PrintHeader();
            return await VerifyCommandAsync();
        }

        if (_args[0] == "commit")
        {
            return await CommitCommandAsync();
        }

        if (_args[0] == "bump")
        {
            return await BumpCommandAsync();
        }

        if (_args[0] == "docs")
        {
            return await DocsCommandAsync();
        }

        if (_args[0] == "release")
        {
            return await ReleaseCommandAsync();
        }

        if (_args[0] == "clean")
        {
            return await CleanCommandAsync();
        }

        if (_args[0] == "--version" || _args[0] == "-v")
        {
            Console.WriteLine($"ando {Version}");
            return 0;
        }

        _logger.Error($"Unknown command: {_args[0]}");
        return 1;
    }

    // Executes the build script - the core functionality of ANDO.
    // This method handles the complete build lifecycle:
    // 1. Locate build script and set up Docker container
    // 2. Load and execute build script with container paths
    // 3. Run the workflow and copy artifacts back
    private async Task<int> RunCommandAsync()
    {
        try
        {
            // Step 1: Find the build script in the current directory.
            // Unlike tools that search parent directories, ANDO requires
            // build.csando to be in the current working directory for clarity.
            var scriptPath = FindBuildScript();

            if (scriptPath == null)
            {
                // Check if user specified a custom file that wasn't found.
                var customFile = GetBuildFileArgument();
                if (customFile != null)
                {
                    // Error already logged in FindBuildScript.
                    return 2;
                }

                // Provide a helpful getting-started example when no script exists.
                _logger.Error("No build.csando found in current directory.");
                _logger.Error("");
                _logger.Error("Create a build.csando file to get started:");
                _logger.Error("");
                _logger.Error("  var project = Dotnet.Project(\"./src/MyApp/MyApp.csproj\");");
                _logger.Error("  Dotnet.Restore(project);");
                _logger.Error("  Dotnet.Build(project);");
                _logger.Error("");
                _logger.Error("Or specify a build file: ando -f mybuild.csando");
                _logger.Error("");
                return 2;
            }

            // Get the default host path for configuration checks.
            // The actual hostRootPath is calculated after DIND check (Step 2c).
            var defaultHostRoot = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;

            // Check that .env.ando is gitignored if it exists.
            // This prevents accidental commits of sensitive environment files.
            if (!CheckEnvAndoGitIgnoreStatus(defaultHostRoot))
            {
                return 6; // Exit code for gitignore check failure
            }

            // Check for .env file and offer to load it.
            await PromptToLoadEnvFileAsync(defaultHostRoot);

            // Step 2: Load and compile the build script using Roslyn.
            // We load first to read Options.Image before creating the container.
            // Use /workspace as the root path since that's where the project is mounted
            // inside the container. This ensures paths in the script resolve correctly.
            const string containerRootPath = "/workspace";
            var scriptHost = new ScriptHost(_logger);

            // Parse and set active profiles before loading script.
            // This allows DefineProfile() calls to check activation state.
            var profiles = GetProfiles();
            scriptHost.SetActiveProfiles(profiles);

            var context = await scriptHost.LoadScriptAsync(scriptPath, containerRootPath);

            // Validate profiles after script loading.
            // This ensures all requested profiles were defined in the script.
            try
            {
                context.ProfileRegistry.Validate();
            }
            catch (InvalidOperationException ex)
            {
                _logger.Error(ex.Message);
                return 4;
            }

            // Log active profiles if any.
            if (profiles.Count > 0)
            {
                _logger.Info($"Profiles: {string.Join(", ", profiles)}");
            }

            // Step 2b: Pre-flight check for GitHub OAuth scopes.
            // This runs on the host before container creation to verify
            // the token has required scopes for operations like PushImage.
            var scopeChecker = new GitHubScopeChecker(_logger);
            if (!await scopeChecker.EnsureRequiredScopesAsync(context.StepRegistry))
            {
                return 7; // Exit code for GitHub scope check failure
            }

            // Step 2c: Pre-flight check for Docker-in-Docker requirements.
            // Detects if build operations need DIND and prompts user if needed.
            var dindChecker = new DindChecker(_logger);
            var dindResult = dindChecker.CheckAndPrompt(
                context.StepRegistry,
                HasFlag("--dind"),
                defaultHostRoot);

            if (dindResult == DindCheckResult.Cancelled)
            {
                return 8; // Exit code for DIND check cancelled
            }

            var enableDind = DindChecker.ShouldEnableDind(dindResult);

            // Calculate hostRootPath based on DIND status.
            // When DIND is enabled, ANDO_HOST_ROOT can override the path for nested containers.
            var hostRootPath = enableDind
                ? Environment.GetEnvironmentVariable("ANDO_HOST_ROOT") ?? defaultHostRoot
                : defaultHostRoot;

            // Step 3: Set up Docker container for isolated execution.
            // All ANDO builds run in Docker containers for reproducibility.
            var dockerManager = new DockerManager(_logger);
            if (!dockerManager.IsDockerAvailable())
            {
                _logger.Error("Docker is not available");
                _logger.Error("");
                _logger.Error("ANDO requires Docker to run builds in isolated containers.");
                _logger.Error("");
                _logger.Error("Install Docker:");
                _logger.Error(dockerManager.GetDockerInstallInstructions());
                _logger.Error("");
                _logger.Error("Or visit: https://docs.docker.com/get-docker/");
                return 3;
            }

            // Configure the Docker container for this project.
            // Container name includes MD5 hash of build script for uniqueness.
            var containerName = GetContainerName(hostRootPath, scriptPath);
            _logger.Info($"Container: {containerName}");

            // In DinD mode, ProjectRoot is the HOST path (for Docker volume mounts)
            // but LocalProjectRoot is the actual path inside this container (for file ops like tar).
            var containerConfig = new ContainerConfig
            {
                Name = containerName,
                ProjectRoot = hostRootPath,
                LocalProjectRoot = enableDind ? defaultHostRoot : null,
                Image = GetDockerImage(context.Options),
                MountDockerSocket = enableDind,  // For building Docker images
            };

            // Warm containers are reused for faster subsequent builds.
            // The --cold flag forces a fresh container (useful for debugging).
            ContainerInfo container;
            if (HasFlag("--cold"))
            {
                await dockerManager.RemoveContainerAsync(containerName);
                container = await dockerManager.EnsureContainerAsync(containerConfig);
            }
            else
            {
                container = await dockerManager.EnsureContainerAsync(containerConfig);
            }

            // Always start with a clean artifacts directory to avoid stale outputs.
            await dockerManager.CleanArtifactsAsync(container.Id);

            // Switch the build context to use container execution.
            // All subsequent commands will run via 'docker exec'.
            var containerExecutor = new ContainerExecutor(container.Id, _logger);
            context.SetExecutor(containerExecutor);
            context.SetDockerManager(dockerManager, container.Id, hostRootPath);

            // Step 4: Execute the workflow.
            // The WorkflowRunner processes all registered steps in order.
            _logger.Verbosity = GetVerbosity();

            var runner = new WorkflowRunner(context.StepRegistry, _logger);
            var result = await runner.RunAsync(context.Options, scriptPath);

            // Step 5: Copy artifacts from container to host.
            // This copies any files registered via Artifacts.CopyToHost().
            if (result.Success)
            {
                await context.CopyArtifactsToHostAsync();
            }

            return result.Success ? 0 : 1;
        }
        catch (FileNotFoundException ex)
        {
            _logger.Error(ex.Message);
            return 2;
        }
        catch (Exception ex) when (ex.Message.Contains("metadata reference") || ex.Message.Contains("assembly without location"))
        {
            // This specific error occurs when Roslyn can't find required assemblies.
            // Most common when running as a single-file executable that needs to
            // extract embedded assemblies to a temp directory.
            _logger.Error("Failed to initialize scripting engine.");
            _logger.Error("");
            _logger.Error("ANDO extracts required files to a temp directory on first run.");
            _logger.Error("This error may occur if:");
            _logger.Error("  - The temp directory is not writable (check permissions)");
            _logger.Error("  - The disk is full");
            _logger.Error("  - The executable was corrupted during download");
            _logger.Error("");
            _logger.Error("Try:");
            _logger.Error("  - Ensure write access to your system temp directory");
            _logger.Error("  - Re-download the executable");
            _logger.Error("");
            if (GetVerbosity() == LogLevel.Detailed)
            {
                _logger.Error($"Details: {ex.Message}");
            }
            return 5;
        }
        catch (Exception ex)
        {
            _logger.Error($"Build failed: {ex.Message}");
            if (GetVerbosity() == LogLevel.Detailed)
            {
                _logger.Error(ex.StackTrace ?? "");
            }
            return 5;
        }
    }

    // Handles the 'verify' command which checks the build script for errors
    // without executing it. Useful for CI validation and editor integration.
    private async Task<int> VerifyCommandAsync()
    {
        var scriptPath = FindBuildScript();

        if (scriptPath == null)
        {
            var customFile = GetBuildFileArgument();
            if (customFile == null)
            {
                _logger.Error("No build.csando found in current directory.");
                    _logger.Error("Or specify a build file: ando verify -f mybuild.csando");
            }
            return 2;
        }

        _logger.Info($"Verifying: {scriptPath}");

        var scriptHost = new ScriptHost(_logger);
        var errors = await scriptHost.VerifyScriptAsync(scriptPath);

        if (errors.Count == 0)
        {
            _logger.Info("Build script is valid.");
            return 0;
        }

        _logger.Error("Build script has errors:");
        foreach (var error in errors)
        {
            _logger.Error($"  {error}");
        }

        return 1;
    }

    // Handles the 'commit' command which commits all changes with an AI-generated message.
    // Uses Claude CLI to analyze the diff and generate a conventional commit message.
    private async Task<int> CommitCommandAsync()
    {
        var runner = new CliProcessRunner();
        var command = new CommitCommand(runner, _logger);
        return await command.ExecuteAsync();
    }

    // Handles the 'bump' command which bumps version in all detected projects.
    // Parses build.csando to find projects and updates their versions.
    private async Task<int> BumpCommandAsync()
    {
        // Parse bump type argument.
        var bumpType = Versioning.BumpType.Patch;
        if (_args.Length > 1)
        {
            bumpType = _args[1].ToLower() switch
            {
                "minor" => Versioning.BumpType.Minor,
                "major" => Versioning.BumpType.Major,
                "patch" => Versioning.BumpType.Patch,
                _ => throw new ArgumentException($"Invalid bump type: {_args[1]}. Use 'patch', 'minor', or 'major'.")
            };
        }

        var runner = new CliProcessRunner();
        var command = new BumpCommand(runner, _logger);
        return await command.ExecuteAsync(bumpType);
    }

    // Handles the 'docs' command which uses Claude to review and update documentation.
    // Analyzes changes since last tag and prompts Claude to update relevant docs.
    private async Task<int> DocsCommandAsync()
    {
        var runner = new CliProcessRunner();
        var command = new DocsCommand(runner, _logger);
        return await command.ExecuteAsync(autoCommit: false);
    }

    // Handles the 'release' command which orchestrates the full release workflow.
    // Presents an interactive checklist for step selection.
    // Supports: ando release [patch|minor|major] [--all] [--dry-run]
    private async Task<int> ReleaseCommandAsync()
    {
        var all = HasFlag("--all");
        var dryRun = HasFlag("--dry-run");

        // Parse bump type from positional argument (e.g., "ando release minor").
        var bumpType = Versioning.BumpType.Patch;
        foreach (var arg in _args.Skip(1)) // Skip "release"
        {
            if (arg.StartsWith("-")) continue; // Skip flags
            bumpType = arg.ToLower() switch
            {
                "minor" => Versioning.BumpType.Minor,
                "major" => Versioning.BumpType.Major,
                "patch" => Versioning.BumpType.Patch,
                _ => bumpType // Ignore unknown positional args
            };
            break; // Only check first positional arg
        }

        var runner = new CliProcessRunner();
        var command = new ReleaseCommand(runner, _logger);
        return await command.ExecuteAsync(all, dryRun, bumpType);
    }

    // Handles the 'clean' command which removes build artifacts and caches.
    // Supports selective cleanup via flags, or cleans artifacts+temp by default.
    private async Task<int> CleanCommandAsync()
    {
        var scriptPath = FindBuildScript();
        var rootPath = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;

        // Remove the Docker container for this project.
        // Useful when you need a fresh build environment.
        if (HasFlag("--container") || HasFlag("--all"))
        {
            var dockerManager = new DockerManager(_logger);
            // Container name requires build script to compute MD5 hash.
            if (scriptPath != null)
            {
                var containerName = GetContainerName(rootPath, scriptPath);
                await dockerManager.RemoveContainerAsync(containerName);
                _logger.Info($"Removed container: {containerName}");
            }
            else
            {
                _logger.Warning("No build.csando found - cannot determine container name");
            }
        }

        // Remove build outputs (binaries, packages, etc.)
        // Default behavior when no specific flags are provided.
        if (HasFlag("--artifacts") || HasFlag("--all") || !HasAnyCleanFlags())
        {
            var artifactsPath = Path.Combine(rootPath, "artifacts");
            if (Directory.Exists(artifactsPath))
            {
                Directory.Delete(artifactsPath, recursive: true);
                _logger.Info($"Removed: {artifactsPath}");
            }
        }

        // Remove temporary files generated during builds.
        // Default behavior when no specific flags are provided.
        if (HasFlag("--temp") || HasFlag("--all") || !HasAnyCleanFlags())
        {
            var tempPath = Path.Combine(rootPath, ".ando", "tmp");
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, recursive: true);
                _logger.Info($"Removed: {tempPath}");
            }
        }

        // Remove cached dependencies (NuGet packages, npm modules).
        // Only removed when explicitly requested, as rebuilding cache is slow.
        if (HasFlag("--cache") || HasFlag("--all"))
        {
            var cachePath = Path.Combine(rootPath, ".ando", "cache");
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, recursive: true);
                _logger.Info($"Removed: {cachePath}");
            }
        }

        return 0;
    }

    // Checks if any specific clean flags were provided.
    // Used to determine default cleanup behavior.
    private bool HasAnyCleanFlags()
    {
        return HasFlag("--artifacts") || HasFlag("--temp") ||
               HasFlag("--cache") || HasFlag("--container") || HasFlag("--all");
    }

    // Generates a deterministic container name using the project directory
    // and MD5 hash of the build file. This ensures:
    // - Container reuse across builds for the same project ("warm containers")
    // - Different containers for different projects even with same directory name
    // - Container invalidation when the build script changes
    private string GetContainerName(string rootPath, string scriptPath)
    {
        var dirName = new DirectoryInfo(rootPath).Name.ToLowerInvariant().Replace(" ", "-");

        // Compute MD5 hash of the build file content for uniqueness.
        // This ensures a new container is used when the build script changes.
        var scriptContent = File.ReadAllBytes(scriptPath);
        var hashBytes = MD5.HashData(scriptContent);
        var hashPrefix = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();

        return $"ando-{dirName}-{hashPrefix}";
    }

    // Gets the Docker image to use.
    // Priority: 1) --image CLI flag, 2) Options.UseImage() from script, 3) default
    private string GetDockerImage(BuildOptions options)
    {
        // CLI flag takes precedence (allows override for debugging).
        var imageIndex = Array.IndexOf(_args, "--image");
        if (imageIndex >= 0 && imageIndex + 1 < _args.Length)
        {
            return _args[imageIndex + 1];
        }

        // Script-specified image via Options.UseImage().
        if (!string.IsNullOrEmpty(options.Image))
        {
            return options.Image;
        }

        // Default is Ubuntu for broad compatibility.
        return "ubuntu:22.04";
    }

    // Simple flag detection helper.
    private bool HasFlag(string flag) => _args.Contains(flag);

    // Parses -p/--profile flags from command line.
    // Supports: -p release, --profile release, -p push,release (comma-separated)
    private List<string> GetProfiles()
    {
        var profiles = new List<string>();

        for (var i = 0; i < _args.Length; i++)
        {
            if ((_args[i] == "-p" || _args[i] == "--profile") && i + 1 < _args.Length)
            {
                // Support comma-separated profiles: -p publish,release
                var value = _args[i + 1];
                profiles.AddRange(value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                i++; // Skip the value on next iteration
            }
        }

        return profiles;
    }

    // Gets the profiles as a comma-separated string for passing to sub-builds.
    private string? GetProfilesArgument()
    {
        var profiles = GetProfiles();
        return profiles.Count > 0 ? string.Join(",", profiles) : null;
    }

    // Displays usage information and available commands/options.
    private int HelpCommand()
    {
        Console.WriteLine("Usage: ando [command] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  run               Run the build script (default command)");
        Console.WriteLine("  verify            Check build script for errors without executing");
        Console.WriteLine("  commit            Commit all changes with AI-generated message");
        Console.WriteLine("  bump [type]       Bump version in all projects (patch|minor|major)");
        Console.WriteLine("  docs              Update documentation using Claude");
        Console.WriteLine("  release [type]    Interactive release workflow (patch|minor|major)");
        Console.WriteLine("  clean             Remove artifacts, temp files, and containers");
        Console.WriteLine("  help, -h, --help  Show this help");
        Console.WriteLine("  -v, --version     Show version number");
        Console.WriteLine();
        Console.WriteLine("Run Options:");
        Console.WriteLine("  -f, --file <file>   Use specific build file instead of build.csando");
        Console.WriteLine("  -p, --profile <name>");
        Console.WriteLine("                      Activate build profiles (comma-separated for multiple)");
        Console.WriteLine("                      Example: -p release or -p publish,release");
        Console.WriteLine("  --read-env          Load env file without prompting (also applies to sub-builds)");
        Console.WriteLine("  --verbosity <level> Set output verbosity: quiet|minimal|normal|detailed");
        Console.WriteLine("  --no-color          Disable colored output (also respects NO_COLOR env var)");
        Console.WriteLine("  --cold              Always create fresh container (ignore warm container)");
        Console.WriteLine("  --image <image>     Use custom Docker image instead of default/script-defined");
        Console.WriteLine("  --dind              Mount Docker socket for Docker-in-Docker operations");
        Console.WriteLine();
        Console.WriteLine("Release Options:");
        Console.WriteLine("  patch               Bump patch version (default): 1.0.0 -> 1.0.1");
        Console.WriteLine("  minor               Bump minor version: 1.0.0 -> 1.1.0");
        Console.WriteLine("  major               Bump major version: 1.0.0 -> 2.0.0");
        Console.WriteLine("  --all               Run all applicable release steps non-interactively");
        Console.WriteLine("  --dry-run           Preview release steps without making changes");
        Console.WriteLine();
        Console.WriteLine("  Release runs a mandatory build verification (ando run) before any steps.");
        Console.WriteLine();
        Console.WriteLine("  Release workflow steps (selectable via interactive checklist):");
        Console.WriteLine("    1. commit   - Commit uncommitted changes (AI-generated message)");
        Console.WriteLine("    2. bump     - Bump version (uses type from command line)");
        Console.WriteLine("    3. docs     - Update documentation using Claude (auto-commits)");
        Console.WriteLine("    4. push     - Push commits to remote");
        Console.WriteLine("    5. publish  - Run build with publish profile (ando run -p publish --dind --read-env)");
        Console.WriteLine();
        Console.WriteLine("Bump Options:");
        Console.WriteLine("  patch               Increment patch version: 1.0.0 -> 1.0.1 (default)");
        Console.WriteLine("  minor               Increment minor version: 1.0.0 -> 1.1.0");
        Console.WriteLine("  major               Increment major version: 1.0.0 -> 2.0.0");
        Console.WriteLine();
        Console.WriteLine("Docs Command:");
        Console.WriteLine("  Uses Claude to analyze code changes and update documentation.");
        Console.WriteLine("  Checks diff since last git tag (or all changes if no tag).");
        Console.WriteLine("  Updates markdown files, website pages, and examples as needed.");
        Console.WriteLine("  Changes are left uncommitted for review (use 'ando commit' after).");
        Console.WriteLine();
        Console.WriteLine("Clean Options:");
        Console.WriteLine("  --artifacts         Remove artifacts directory");
        Console.WriteLine("  --temp              Remove .ando/tmp directory");
        Console.WriteLine("  --cache             Remove .ando/cache (NuGet/npm caches)");
        Console.WriteLine("  --container         Remove the project's warm Docker container");
        Console.WriteLine("  --all               Remove all of the above");
        Console.WriteLine("  (no flags)          Default: remove artifacts and temp only");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine("  ando                          Run build.csando in current directory");
        Console.WriteLine("  ando run -p release           Run with 'release' profile activated");
        Console.WriteLine("  ando -f deploy.csando         Run a specific build file");
        Console.WriteLine("  ando verify                   Verify build script syntax");
        Console.WriteLine("  ando commit                   Commit with AI-generated message");
        Console.WriteLine("  ando bump                     Bump patch version");
        Console.WriteLine("  ando bump minor               Bump minor version");
        Console.WriteLine("  ando docs                     Update documentation with Claude");
        Console.WriteLine("  ando release                  Interactive release workflow (patch bump)");
        Console.WriteLine("  ando release minor            Release with minor version bump");
        Console.WriteLine("  ando release major --all      Release major version, skip checklist");
        Console.WriteLine("  ando release --dry-run        Preview release without changes");
        Console.WriteLine("  ando clean                    Remove artifacts and temp files");
        Console.WriteLine("  ando clean --all              Remove everything including container");
        Console.WriteLine();
        Console.WriteLine("Exit Codes:");
        Console.WriteLine("  0  Success");
        Console.WriteLine("  1  Build workflow failed");
        Console.WriteLine("  2  Script not found or invalid file");
        Console.WriteLine("  3  Docker not available");
        Console.WriteLine("  4  Profile validation failed (undefined profile requested)");
        Console.WriteLine("  5  Generic error (Roslyn errors, exceptions)");
        Console.WriteLine("  6  .env.ando not gitignored (user declined to continue)");
        Console.WriteLine("  7  GitHub OAuth scope check failed");
        Console.WriteLine("  8  DIND check cancelled (user pressed Escape)");
        Console.WriteLine();
        Console.WriteLine("Environment Files:");
        Console.WriteLine("  ANDO checks for .env.ando (preferred) or .env in the project root.");
        Console.WriteLine("  When found, prompts to load environment variables before the build.");
        Console.WriteLine("  Use --read-env to auto-load without prompting.");
        Console.WriteLine();
        Console.WriteLine("Note: All builds run in Docker containers for reproducibility.");
        Console.WriteLine();
        return 0;
    }

    // Locates the build script file.
    // If --file is provided, uses that file.
    // Otherwise looks for build.csando in the current directory.
    // Returns null if not found.
    private string? FindBuildScript()
    {
        // Check if a custom build file was specified via --file or -f.
        var customFile = GetBuildFileArgument();
        if (customFile != null)
        {
            // Resolve relative to current directory.
            var fullPath = Path.IsPathRooted(customFile)
                ? customFile
                : Path.Combine(Environment.CurrentDirectory, customFile);

            if (File.Exists(fullPath))
            {
                return fullPath;
            }

            _logger.Error($"Build file not found: {customFile}");
            return null;
        }

        // Default: look for build.csando in current directory.
        var buildScript = Path.Combine(Environment.CurrentDirectory, "build.csando");
        return File.Exists(buildScript) ? buildScript : null;
    }

    // Gets the build file argument from --file or -f flag.
    private string? GetBuildFileArgument()
    {
        var fileIndex = Array.IndexOf(_args, "--file");
        if (fileIndex < 0)
        {
            fileIndex = Array.IndexOf(_args, "-f");
        }

        if (fileIndex >= 0 && fileIndex + 1 < _args.Length)
        {
            return _args[fileIndex + 1];
        }

        return null;
    }

    // Parses the --verbosity flag to determine log level.
    // Supports: quiet, minimal, normal (default), detailed
    private LogLevel GetVerbosity()
    {
        var verbosityIndex = Array.IndexOf(_args, "--verbosity");
        if (verbosityIndex >= 0 && verbosityIndex + 1 < _args.Length)
        {
            return _args[verbosityIndex + 1].ToLowerInvariant() switch
            {
                "quiet" => LogLevel.Quiet,
                "minimal" => LogLevel.Minimal,
                "normal" => LogLevel.Normal,
                "detailed" => LogLevel.Detailed,
                _ => LogLevel.Normal
            };
        }
        return LogLevel.Normal;
    }

    // Checks if color output should be disabled.
    // Respects both --no-color flag and the NO_COLOR environment variable
    // (see https://no-color.org/ for the standard).
    private static bool IsNoColorRequested(string[] args)
    {
        return args.Contains("--no-color") ||
               Environment.GetEnvironmentVariable("NO_COLOR") != null;
    }

    // Environment variable to auto-load .env files without prompting (for sub-builds).
    private const string AutoLoadEnvVar = "ANDO_AUTO_LOAD_ENV";

    // Checks if .env.ando file exists and is properly gitignored.
    // If the file exists but is not gitignored, warns the user and prompts
    // for confirmation to continue. This prevents accidental commits of
    // sensitive environment files.
    // Returns true if safe to continue, false if user wants to abort.
    private bool CheckEnvAndoGitIgnoreStatus(string rootPath)
    {
        var envAndoPath = Path.Combine(rootPath, ".env.ando");

        // If .env.ando doesn't exist, nothing to check.
        if (!File.Exists(envAndoPath))
            return true;

        // Check if we're in a git repository.
        var gitDir = Path.Combine(rootPath, ".git");
        if (!Directory.Exists(gitDir))
            return true; // Not a git repo, skip check.

        // Use git check-ignore to verify .env.ando is gitignored.
        // Exit code 0 = file is ignored, non-zero = file is not ignored.
        try
        {
            var processInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "git",
                Arguments = "check-ignore -q .env.ando",
                WorkingDirectory = rootPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = System.Diagnostics.Process.Start(processInfo);
            if (process == null)
                return true; // Can't run git, skip check.

            process.WaitForExit(5000); // 5 second timeout

            if (process.ExitCode == 0)
                return true; // File is gitignored, safe to continue.
        }
        catch
        {
            // Git not available or other error, skip check.
            return true;
        }

        // File exists but is NOT gitignored - warn the user.
        _logger.Warning("WARNING: .env.ando file is not gitignored!");
        _logger.Warning("");
        _logger.Warning("The .env.ando file typically contains sensitive information such as");
        _logger.Warning("API keys, tokens, and passwords. It should be added to .gitignore");
        _logger.Warning("to prevent accidental commits.");
        _logger.Warning("");

        // Prompt user with options. (A) is default when Enter is pressed.
        Console.Write("(A)dd to .gitignore (default), (c)ontinue anyway, Esc to exit: ");
        var keyInfo = Console.ReadKey(intercept: true);

        // Handle Escape key.
        if (keyInfo.Key == ConsoleKey.Escape)
        {
            Console.WriteLine();
            _logger.Info("Build aborted.");
            return false;
        }

        // Handle Enter (default to 'a') or explicit key press.
        var keyChar = keyInfo.Key == ConsoleKey.Enter ? 'a' : char.ToLowerInvariant(keyInfo.KeyChar);
        Console.WriteLine(keyInfo.Key == ConsoleKey.Enter ? "" : keyInfo.KeyChar.ToString());

        switch (keyChar)
        {
            case 'a':
                // Add .env.ando to .gitignore.
                var gitignorePath = Path.Combine(rootPath, ".gitignore");
                try
                {
                    // Append to existing .gitignore or create new one.
                    var existingContent = File.Exists(gitignorePath) ? File.ReadAllText(gitignorePath) : "";
                    var newLine = existingContent.EndsWith('\n') || string.IsNullOrEmpty(existingContent) ? "" : "\n";
                    File.AppendAllText(gitignorePath, $"{newLine}.env.ando\n");
                    _logger.Info("Added .env.ando to .gitignore");
                    Console.WriteLine();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Failed to update .gitignore: {ex.Message}");
                    return false;
                }

            case 'c':
                Console.WriteLine();
                return true;

            default:
                _logger.Info("Build aborted.");
                return false;
        }
    }

    // Checks for an environment file in the project directory and prompts the user
    // to load it if found. This provides a convenient way to set environment
    // variables for local development without exporting them in the shell.
    // Checks for .env.ando first (project-specific), falls back to .env if not found.
    private async Task PromptToLoadEnvFileAsync(string rootPath)
    {
        // Check for .env.ando first (takes priority), then fall back to .env.
        var envPath = ResolveEnvFilePath(rootPath);
        if (envPath == null)
            return;

        var envFileName = Path.GetFileName(envPath);

        // Parse the env file to extract variable names and values.
        var envVars = ParseEnvFile(envPath);
        if (envVars.Count == 0)
            return;

        // Check if any variables are already set - if all are set, skip prompting.
        var unsetVars = envVars.Where(kv => Environment.GetEnvironmentVariable(kv.Key) == null).ToList();
        if (unsetVars.Count == 0)
            return;

        // Check if auto-load is enabled:
        // 1. --read-env CLI flag was passed
        // 2. ANDO_AUTO_LOAD_ENV=1 (set by parent build when user chose "for this run")
        // 3. ando.config has readEnv: true
        var config = ProjectConfig.Load(rootPath);
        var autoLoad = HasFlag("--read-env") ||
                       Environment.GetEnvironmentVariable(AutoLoadEnvVar) == "1" ||
                       config.ReadEnv;

        // If --read-env was passed or config has readEnv, also enable for sub-builds.
        if (HasFlag("--read-env") || config.ReadEnv)
        {
            Environment.SetEnvironmentVariable(AutoLoadEnvVar, "1");
        }

        if (!autoLoad)
        {
            // Show the user which variables would be loaded.
            _logger.Info($"Found {envFileName} file with the following variables:");
            foreach (var (key, value) in unsetVars)
            {
                // Mask sensitive values (tokens, secrets, passwords, keys).
                var isSensitive = key.Contains("TOKEN", StringComparison.OrdinalIgnoreCase) ||
                                  key.Contains("SECRET", StringComparison.OrdinalIgnoreCase) ||
                                  key.Contains("PASSWORD", StringComparison.OrdinalIgnoreCase) ||
                                  key.Contains("KEY", StringComparison.OrdinalIgnoreCase);
                var displayValue = isSensitive ? "********" : value;
                _logger.Info($"  {key}={displayValue}");
            }

            // Prompt the user.
            // Y = yes for this build only
            // r = yes for this run and all sub-builds
            // a = always (save to ando.config)
            // n = no
            Console.Write("Load these environment variables? [(Y)es/(n)o/for this (r)un/(a)lways] ");
            var response = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (response == "n" || response == "no")
            {
                _logger.Info($"Skipped loading {envFileName}");
                Console.WriteLine();
                return;
            }

            // Enable auto-load for sub-builds if user chose "for this run".
            if (response == "r" || response == "run")
            {
                Environment.SetEnvironmentVariable(AutoLoadEnvVar, "1");
                _logger.Info("Enabled auto-load for this and all sub-builds");
            }

            // Save to ando.config if user chose "always".
            if (response == "a" || response == "always")
            {
                var newConfig = config with { ReadEnv = true };
                newConfig.Save(rootPath);
                Environment.SetEnvironmentVariable(AutoLoadEnvVar, "1");
                _logger.Info("Saved readEnv:true to ando.config");
            }
        }

        // Load the environment variables.
        foreach (var (key, value) in unsetVars)
        {
            Environment.SetEnvironmentVariable(key, value);
        }
        _logger.Info($"Loaded {unsetVars.Count} environment variable(s) from {envFileName}");

        Console.WriteLine();
        await Task.CompletedTask;
    }

    // Resolves which environment file to use.
    // Checks for .env.ando first (project-specific), falls back to .env.
    // Returns null if neither file exists.
    internal static string? ResolveEnvFilePath(string rootPath)
    {
        var andoEnvPath = Path.Combine(rootPath, ".env.ando");
        if (File.Exists(andoEnvPath))
            return andoEnvPath;

        var defaultEnvPath = Path.Combine(rootPath, ".env");
        if (File.Exists(defaultEnvPath))
            return defaultEnvPath;

        return null;
    }

    // Parses a .env file into key-value pairs.
    // Supports:
    // - KEY=VALUE format
    // - Comments starting with #
    // - Quoted values (single or double quotes)
    // - Empty lines are ignored
    internal static Dictionary<string, string> ParseEnvFile(string path)
    {
        var result = new Dictionary<string, string>();

        foreach (var line in File.ReadAllLines(path))
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments.
            if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith('#'))
                continue;

            // Find the first = sign.
            var equalsIndex = trimmed.IndexOf('=');
            if (equalsIndex <= 0)
                continue;

            var key = trimmed[..equalsIndex].Trim();
            var value = trimmed[(equalsIndex + 1)..].Trim();

            // Remove surrounding quotes if present.
            if ((value.StartsWith('"') && value.EndsWith('"')) ||
                (value.StartsWith('\'') && value.EndsWith('\'')))
            {
                value = value[1..^1];
            }

            result[key] = value;
        }

        return result;
    }
}
