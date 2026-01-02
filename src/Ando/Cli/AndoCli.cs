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
// =============================================================================

using System.Reflection;
using System.Security.Cryptography;
using Ando.Execution;
using Ando.Logging;
using Ando.Scripting;
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
        var logPath = Path.Combine(Environment.CurrentDirectory, "build.ando.log");

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
    private void PrintHeader()
    {
        var useColor = !IsNoColorRequested(_args);
        var cyan = useColor ? "\u001b[1;36m" : "";
        var gray = useColor ? "\u001b[2;37m" : "";
        var reset = useColor ? "\u001b[0m" : "";

        Console.WriteLine();
        Console.WriteLine($"{cyan}   _   _  _ ___   ___  {reset}");
        Console.WriteLine($"{cyan}  /_\\ | \\| |   \\ / _ \\ {reset}");
        Console.WriteLine($"{cyan} / _ \\| .` | |) | (_) |{reset}");
        Console.WriteLine($"{cyan}/_/ \\_\\_|\\_|___/ \\___/ {reset}{gray}v{Version}{reset}");
        Console.WriteLine();
        Console.WriteLine($"Minimal & Fast Build System");
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
        // This allows "ando" and "ando run" to behave identically.
        if (_args.Length == 0 || _args[0] == "run" || !_args[0].StartsWith("-") && _args[0] != "clean" && _args[0] != "help")
        {
            PrintHeader();
            return await RunCommandAsync();
        }

        if (_args[0] == "help" || _args[0] == "--help" || _args[0] == "-h")
        {
            PrintHeader();
            return HelpCommand();
        }

        if (_args[0] == "clean")
        {
            return await CleanCommandAsync();
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
            // build.ando to be in the current working directory for clarity.
            var scriptPath = FindBuildScript();

            if (scriptPath == null)
            {
                // Provide a helpful getting-started example when no script exists.
                _logger.Error("No build.ando found in current directory.");
                _logger.Error("");
                _logger.Error("Create a build.ando file to get started:");
                _logger.Error("");
                _logger.Error("  var project = Project.From(\"./src/MyApp/MyApp.csproj\");");
                _logger.Error("  Dotnet.Restore(project);");
                _logger.Error("  Dotnet.Build(project);");
                _logger.Error("");
                return 2;
            }

            // Get the host path for Docker configuration.
            var hostRootPath = Path.GetDirectoryName(scriptPath) ?? Environment.CurrentDirectory;

            // Step 2: Set up Docker container for isolated execution.
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
            var containerConfig = new ContainerConfig
            {
                Name = containerName,
                ProjectRoot = hostRootPath,
                Image = GetDockerImage(),
                MountDockerSocket = HasFlag("--dind"),  // For building Docker images
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

            // Step 3: Load and compile the build script using Roslyn.
            // Use /workspace as the root path since that's where the project is mounted
            // inside the container. This ensures paths in the script resolve correctly.
            const string containerRootPath = "/workspace";
            var scriptHost = new ScriptHost(_logger);
            var context = await scriptHost.LoadScriptAsync(scriptPath, containerRootPath);

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
                _logger.Warning("No build.ando found - cannot determine container name");
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

    // Gets the Docker image to use, either from --image flag or default.
    // Default is the .NET SDK alpine image for minimal size.
    private string GetDockerImage()
    {
        var imageIndex = Array.IndexOf(_args, "--image");
        if (imageIndex >= 0 && imageIndex + 1 < _args.Length)
        {
            return _args[imageIndex + 1];
        }
        return "mcr.microsoft.com/dotnet/sdk:9.0-alpine";
    }

    // Simple flag detection helper.
    private bool HasFlag(string flag) => _args.Contains(flag);

    // Displays usage information and available commands/options.
    private int HelpCommand()
    {
        Console.WriteLine("Usage: ando [command] [options]");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  run               Run the build script (default)");
        Console.WriteLine("  clean             Remove artifacts, temp files, and containers");
        Console.WriteLine("  help              Show this help");
        Console.WriteLine();
        Console.WriteLine("Run Options:");
        Console.WriteLine("  --verbosity <quiet|minimal|normal|detailed>");
        Console.WriteLine("  --no-color        Disable colored output");
        Console.WriteLine("  --cold            Always create fresh container");
        Console.WriteLine("  --image <image>   Use custom Docker image");
        Console.WriteLine("  --dind            Mount Docker socket for Docker-in-Docker");
        Console.WriteLine();
        Console.WriteLine("Clean Options:");
        Console.WriteLine("  --artifacts       Remove artifacts directory");
        Console.WriteLine("  --temp            Remove temp directory");
        Console.WriteLine("  --cache           Remove NuGet and npm caches");
        Console.WriteLine("  --container       Remove the project's warm container");
        Console.WriteLine("  --all             Remove all of the above");
        Console.WriteLine();
        Console.WriteLine("Note: All builds run in Docker containers for reproducibility.");
        Console.WriteLine();
        return 0;
    }

    // Locates the build.ando file in the current directory.
    // Returns null if not found - does not search parent directories
    // to keep build behavior predictable and explicit.
    private string? FindBuildScript()
    {
        var buildScript = Path.Combine(Environment.CurrentDirectory, "build.ando");
        return File.Exists(buildScript) ? buildScript : null;
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
}
