// =============================================================================
// BuildContext.cs
//
// Summary: Container for all build state and operations used during script execution.
//
// BuildContext holds everything needed during a build: the step registry,
// operations (Dotnet, Ef, Npm, Artifacts), command executor, Docker manager,
// and logger. It's created by ScriptHost and passed to ScriptGlobals to
// expose the ANDO API.
//
// Architecture:
// - Created once per build and passed through the entire build lifecycle
// - Operations use a factory lambda () => Executor to get the current executor
// - Executor can be switched from ProcessRunner to ContainerExecutor after script load
// - StepRegistry accumulates steps as the script is executed
// - DockerManager handles container lifecycle and artifact copying
//
// Design Decisions:
// - Single object holds all build state for easy passing between components
// - Executor is mutable to allow switching to container execution after script load
// - Operations receive executor factory (not executor directly) to support swapping
// - Artifacts are tracked during build and copied to host after successful execution
// =============================================================================

using Ando.Context;
using Ando.Execution;
using Ando.Logging;
using Ando.Operations;
using Ando.Steps;
using Ando.Workflow;

namespace Ando.Scripting;

/// <summary>
/// Container for all build state and operations used during script execution.
/// Created by ScriptHost and used throughout the build lifecycle.
/// </summary>
public class BuildContext
{
    /// <summary>Unified context with Paths and Vars.</summary>
    public BuildContextObject Context { get; }

    /// <summary>Build configuration options.</summary>
    public BuildOptions Options { get; }

    /// <summary>Container for all build operations.</summary>
    public BuildOperations Operations { get; }

    /// <summary>Registry of steps to execute.</summary>
    public StepRegistry StepRegistry { get; }

    /// <summary>Logger for build output.</summary>
    public IBuildLogger Logger { get; }

    /// <summary>Current command executor (local or container).</summary>
    public ICommandExecutor Executor { get; private set; }

    // Convenience accessors for operations (backward compatibility).
    // These delegate to the Operations container.

    /// <summary>.NET CLI operations.</summary>
    public DotnetOperations Dotnet => Operations.Dotnet;

    /// <summary>Entity Framework operations.</summary>
    public EfOperations Ef => Operations.Ef;

    /// <summary>npm operations.</summary>
    public NpmOperations Npm => Operations.Npm;

    /// <summary>Azure CLI authentication and account operations.</summary>
    public AzureOperations Azure => Operations.Azure;

    /// <summary>Azure Bicep deployment operations.</summary>
    public BicepOperations Bicep => Operations.Bicep;

    /// <summary>Cloudflare operations (Pages deployment, etc.).</summary>
    public CloudflareOperations Cloudflare => Operations.Cloudflare;

    /// <summary>Azure Functions deployment operations.</summary>
    public FunctionsOperations Functions => Operations.Functions;

    /// <summary>Azure App Service deployment operations.</summary>
    public AppServiceOperations AppService => Operations.AppService;

    /// <summary>Artifact operations for copying files to host.</summary>
    public ArtifactOperations Artifacts => Operations.Artifacts;

    /// <summary>Node.js installation operations (installs Node.js globally).</summary>
    public NodeInstallOperations Node => Operations.Node;

    /// <summary>.NET SDK installation operations (installs SDK globally).</summary>
    public DotnetInstallOperations DotnetSdk => Operations.DotnetSdk;

    /// <summary>Logging operations for build script output.</summary>
    public LogOperations Log => Operations.Log;

    /// <summary>NuGet package operations (pack, push).</summary>
    public NugetOperations Nuget => Operations.Nuget;

    /// <summary>ANDO operations for nested builds.</summary>
    public AndoOperations Ando => Operations.Ando;

    // Docker manager, container ID, and host root path for artifact copying.
    // Set after container is created via SetDockerManager().
    private DockerManager? _dockerManager;
    private string? _containerId;
    private string? _hostRootPath;

    // Container root path for path translation.
    private readonly string _containerRootPath;

    public BuildContext(string rootPath, IBuildLogger logger)
    {
        _containerRootPath = rootPath;
        Context = new BuildContextObject(rootPath);
        Options = new BuildOptions();
        StepRegistry = new StepRegistry();
        Logger = logger;

        // Default to local process execution.
        // Will be replaced with ContainerExecutor when Docker mode is active.
        Executor = new ProcessRunner(logger);

        // Create operations container with an executor factory lambda.
        // The factory ensures operations always get the current executor,
        // even if it's changed after script loading.
        Operations = new BuildOperations(StepRegistry, Logger, () => Executor, Context.Vars, TranslateContainerToHostPath);
    }

    /// <summary>
    /// Translates a container path to a host path.
    /// Used by Ando.Build to run child builds on the host.
    /// </summary>
    private string TranslateContainerToHostPath(string containerPath)
    {
        // If no host root configured, return path as-is (local mode).
        if (string.IsNullOrEmpty(_hostRootPath))
        {
            return containerPath;
        }

        // If path starts with container root, translate to host path.
        if (containerPath.StartsWith(_containerRootPath))
        {
            var relativePath = containerPath[_containerRootPath.Length..].TrimStart('/', '\\');
            return Path.Combine(_hostRootPath, relativePath);
        }

        // For relative paths, combine with host root.
        if (!Path.IsPathRooted(containerPath))
        {
            return Path.Combine(_hostRootPath, containerPath);
        }

        // Return unchanged for other absolute paths (may not work correctly).
        Logger.Warning($"Cannot translate path '{containerPath}' - returning unchanged");
        return containerPath;
    }

    /// <summary>
    /// Sets the command executor (e.g., when switching to container execution).
    /// Called by CLI after script loading when Docker mode is enabled.
    /// </summary>
    public void SetExecutor(ICommandExecutor executor)
    {
        Executor = executor;
    }

    /// <summary>
    /// Sets the Docker manager, container ID, and host root path for artifact operations.
    /// Called by CLI after container is created.
    /// </summary>
    /// <param name="dockerManager">The Docker manager instance.</param>
    /// <param name="containerId">The container ID for docker exec/cp commands.</param>
    /// <param name="hostRootPath">The host path where the project is located.</param>
    public void SetDockerManager(DockerManager dockerManager, string containerId, string hostRootPath)
    {
        _dockerManager = dockerManager;
        _containerId = containerId;
        _hostRootPath = hostRootPath;
    }

    /// <summary>
    /// Copies registered artifacts from the container to the host.
    /// Called after successful build completion.
    /// </summary>
    public async Task CopyArtifactsToHostAsync()
    {
        if (_dockerManager == null || _containerId == null || _hostRootPath == null)
        {
            Logger.Debug("No Docker manager configured - skipping artifact copy");
            return;
        }

        // Use the host root path for copying artifacts back to the host filesystem.
        await Artifacts.CopyToHostAsync(_containerId, _hostRootPath, Logger);
    }
}
