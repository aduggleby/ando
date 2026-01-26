// =============================================================================
// AndoOperations.cs
//
// Summary: Core ANDO operations for build configuration and nested builds.
//
// AndoOperations provides:
// - UseImage: Set the Docker image for the current build
// - CopyArtifactsToHost: Copy files from container to host after build
// - Build: Run child build scripts in subdirectories
//
// Architecture:
// - UseImage sets the image before container creation (not a step)
// - CopyArtifactsToHost registers artifacts for post-build copying
// - Child builds run via `ando run` spawned on the HOST machine
// - Each child gets a fresh container (not shared with parent)
//
// Design Decisions:
// - UseImage is immediate (not a registered step) because it affects container creation
// - CopyArtifactsToHost delegates to ArtifactOperations for actual copying
// - Path translation needed since script sees container paths
// - Child build appears as single grouped step in parent workflow
// =============================================================================

using Ando.Logging;
using Ando.Profiles;
using Ando.References;
using Ando.Steps;
using Ando.Workflow;

namespace Ando.Operations;

/// <summary>
/// Core ANDO operations for build configuration, artifacts, and nested builds.
/// </summary>
public class AndoOperations
{
    private readonly StepRegistry _registry;
    private readonly IBuildLogger _logger;
    private readonly Func<string, string> _containerToHostPath;
    private readonly BuildOptions _buildOptions;
    private readonly ArtifactOperations _artifactOperations;
    private readonly ProfileRegistry _profileRegistry;

    /// <summary>
    /// Creates a new AndoOperations instance.
    /// </summary>
    /// <param name="registry">Step registry for registering build steps.</param>
    /// <param name="logger">Logger for output.</param>
    /// <param name="containerToHostPath">Function to translate container paths to host paths.</param>
    /// <param name="buildOptions">Build options for configuring the current build.</param>
    /// <param name="artifactOperations">Artifact operations for copying files to host.</param>
    /// <param name="profileRegistry">Profile registry for passing profiles to sub-builds.</param>
    public AndoOperations(
        StepRegistry registry,
        IBuildLogger logger,
        Func<string, string> containerToHostPath,
        BuildOptions buildOptions,
        ArtifactOperations artifactOperations,
        ProfileRegistry profileRegistry)
    {
        _registry = registry;
        _logger = logger;
        _containerToHostPath = containerToHostPath;
        _buildOptions = buildOptions;
        _artifactOperations = artifactOperations;
        _profileRegistry = profileRegistry;
    }

    /// <summary>
    /// Sets the Docker image for the current build container.
    /// Must be called before any build steps execute.
    /// </summary>
    /// <param name="image">Docker image name (e.g., "ubuntu:24.04", "mcr.microsoft.com/dotnet/sdk:9.0").</param>
    public void UseImage(string image)
    {
        _buildOptions.UseImage(image);
        _logger.Debug($"Set build image: {image}");
    }

    /// <summary>
    /// Registers files or directories to be copied from the container to the host
    /// after the build completes successfully.
    /// </summary>
    /// <param name="containerPath">
    /// Path inside the container. Can be relative to /workspace (e.g., "dist", "./artifacts")
    /// or absolute (e.g., "/workspace/dist").
    /// </param>
    /// <param name="hostPath">
    /// Destination path on the host machine. Can be relative to project root (e.g., "./dist")
    /// or absolute.
    /// </param>
    public void CopyArtifactsToHost(string containerPath, string hostPath)
    {
        _artifactOperations.CopyToHost(containerPath, hostPath);
    }

    /// <summary>
    /// Registers files or directories to be archived and copied from the container
    /// to the host after the build completes successfully. Creates a single archive file
    /// for faster transfer of many small files.
    /// </summary>
    /// <param name="containerPath">
    /// Path inside the container. Can be relative to /workspace (e.g., "dist", "./artifacts")
    /// or absolute (e.g., "/workspace/dist").
    /// </param>
    /// <param name="hostPath">
    /// Destination on the host machine. Can be:
    /// - A directory path (e.g., "./dist") - creates "artifacts.tar.gz" in that directory
    /// - A .tar.gz file path (e.g., "./dist/output.tar.gz") - creates tar.gz archive
    /// - A .zip file path (e.g., "./dist/output.zip") - creates zip archive
    /// </param>
    public void CopyZippedArtifactsToHost(string containerPath, string hostPath)
    {
        _artifactOperations.CopyZippedToHost(containerPath, hostPath);
    }

    /// <summary>
    /// Runs a build script in the specified directory or file.
    /// The child build runs in a new isolated container with its own .env and context.
    /// </summary>
    /// <param name="directory">
    /// Directory containing the build.csando file, or a path to a specific .csando file.
    /// Examples:
    /// - Directory("./website") - runs build.csando in ./website
    /// - Directory("./website") / "deploy.csando" - runs deploy.csando in ./website
    /// </param>
    /// <param name="configure">Optional configuration for the child build.</param>
    public void Build(DirectoryRef directory, Action<AndoBuildOptions>? configure = null)
    {
        var dirPath = directory.Path;
        var isFile = dirPath.EndsWith(".csando", StringComparison.OrdinalIgnoreCase);

        // For display name, use the file name or directory name.
        var displayName = isFile
            ? Path.GetFileName(dirPath)
            : directory.Name;

        _registry.Register($"Ando.Build", async () =>
        {
            // Apply options if provided.
            var options = new AndoBuildOptions();
            configure?.Invoke(options);

            // Translate container path to host path for spawning the child process.
            var hostPath = _containerToHostPath(dirPath);

            // Determine working directory and build file argument.
            string workingDirectory;
            string? buildFileArg = null;

            if (isFile)
            {
                // Path is a file - run in parent directory with file as argument.
                workingDirectory = Path.GetDirectoryName(hostPath) ?? hostPath;
                buildFileArg = Path.GetFileName(hostPath);
                _logger.Debug($"Running child build: {buildFileArg} in {workingDirectory}");
            }
            else
            {
                // Path is a directory - run in that directory.
                workingDirectory = hostPath;
                _logger.Debug($"Running child build in: {workingDirectory}");
            }

            // Build CLI arguments from options.
            var argsList = options.BuildArgs().ToList();

            // If a specific build file was specified, add it to the arguments.
            if (buildFileArg != null)
            {
                argsList.InsertRange(0, new[] { "-f", buildFileArg });
            }

            // Pass active profiles to child build, but only if not explicitly overridden.
            // If WithProfile() was called, use that instead of inheriting from parent.
            if (string.IsNullOrEmpty(options.Profile))
            {
                var activeProfiles = _profileRegistry.ActiveProfiles;
                if (activeProfiles.Count > 0)
                {
                    argsList.Add("-p");
                    argsList.Add(string.Join(",", activeProfiles));
                }
            }

            var args = argsList.ToArray();

            // Run ando on the host machine - it will create its own container.
            // Using ProcessRunner directly since we need host execution.
            // Interactive mode allows child builds to prompt for user input.
            var runner = new Execution.ProcessRunner(_logger);

            // Calculate the indent level for the child build.
            // Read current level from env var (default 0), increment by 1.
            var currentIndentStr = Environment.GetEnvironmentVariable(Logging.ConsoleLogger.IndentLevelEnvVar);
            var currentIndent = int.TryParse(currentIndentStr, out var level) ? level : 0;
            var childIndent = currentIndent + 1;

            var commandOptions = new Execution.CommandOptions
            {
                WorkingDirectory = workingDirectory,
                Interactive = true,
                TimeoutMs = Execution.CommandOptions.NoTimeout
            };

            // Set indent level for child build output formatting.
            commandOptions.Environment[Logging.ConsoleLogger.IndentLevelEnvVar] = childIndent.ToString();

            var result = await runner.ExecuteAsync("ando", args, commandOptions);

            return result.Success;
        }, displayName);
    }
}
