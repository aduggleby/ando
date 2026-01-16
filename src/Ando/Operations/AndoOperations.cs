// =============================================================================
// AndoOperations.cs
//
// Summary: Operations for running nested ANDO builds.
//
// AndoOperations provides the ability to run child build scripts from within
// a parent build. Each child build runs in its own isolated container with
// its own .env file and context.
//
// Architecture:
// - Child builds run via `ando run` spawned on the HOST machine
// - Each child gets a fresh container (not shared with parent)
// - Child reads its own .env file from its directory
// - Child's Context.Vars are isolated from parent
//
// Design Decisions:
// - Uses ProcessRunner for host execution (not ContainerExecutor)
// - Path translation needed since script sees container paths
// - Child build appears as single grouped step in parent workflow
// =============================================================================

using Ando.Logging;
using Ando.References;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Operations for running nested ANDO builds.
/// Allows a parent build script to invoke child build scripts in subdirectories.
/// </summary>
public class AndoOperations
{
    private readonly StepRegistry _registry;
    private readonly IBuildLogger _logger;
    private readonly Func<string, string> _containerToHostPath;

    /// <summary>
    /// Creates a new AndoOperations instance.
    /// </summary>
    /// <param name="registry">Step registry for registering build steps.</param>
    /// <param name="logger">Logger for output.</param>
    /// <param name="containerToHostPath">Function to translate container paths to host paths.</param>
    public AndoOperations(
        StepRegistry registry,
        IBuildLogger logger,
        Func<string, string> containerToHostPath)
    {
        _registry = registry;
        _logger = logger;
        _containerToHostPath = containerToHostPath;
    }

    /// <summary>
    /// Runs the build.ando script in the specified directory.
    /// The child build runs in a new isolated container with its own .env and context.
    /// </summary>
    /// <param name="directory">Directory containing the build.ando file.</param>
    /// <param name="configure">Optional configuration for the child build.</param>
    public void Build(DirectoryRef directory, Action<AndoBuildOptions>? configure = null)
    {
        var dirName = directory.Name;

        _registry.Register($"Ando.Build", async () =>
        {
            // Apply options if provided.
            var options = new AndoBuildOptions();
            configure?.Invoke(options);

            // Translate container path to host path for spawning the child process.
            var hostPath = _containerToHostPath(directory.Path);

            _logger.Debug($"Running child build in: {hostPath}");

            // Build CLI arguments from options.
            var args = options.BuildArgs();

            // Run ando on the host machine - it will create its own container.
            // Using ProcessRunner directly since we need host execution.
            var runner = new Execution.ProcessRunner(_logger);

            var result = await runner.ExecuteAsync(
                "ando",
                args,
                new Execution.CommandOptions { WorkingDirectory = hostPath });

            return result.Success;
        }, dirName);
    }
}
