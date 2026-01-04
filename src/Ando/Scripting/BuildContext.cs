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

    /// <summary>.NET CLI operations.</summary>
    public DotnetOperations Dotnet { get; }

    /// <summary>Entity Framework operations.</summary>
    public EfOperations Ef { get; }

    /// <summary>npm operations.</summary>
    public NpmOperations Npm { get; }

    /// <summary>Azure CLI authentication and account operations.</summary>
    public AzureOperations Azure { get; }

    /// <summary>Azure Bicep deployment operations.</summary>
    public BicepOperations Bicep { get; }

    /// <summary>Cloudflare operations (Pages deployment, etc.).</summary>
    public CloudflareOperations Cloudflare { get; }

    /// <summary>Artifact operations for copying files to host.</summary>
    public ArtifactOperations Artifacts { get; }

    /// <summary>Registry of steps to execute.</summary>
    public StepRegistry StepRegistry { get; }

    /// <summary>Logger for build output.</summary>
    public IBuildLogger Logger { get; }

    /// <summary>Current command executor (local or container).</summary>
    public ICommandExecutor Executor { get; private set; }

    // Docker manager, container ID, and host root path for artifact copying.
    // Set after container is created via SetDockerManager().
    private DockerManager? _dockerManager;
    private string? _containerId;
    private string? _hostRootPath;

    public BuildContext(string rootPath, IBuildLogger logger)
    {
        Context = new BuildContextObject(rootPath);
        Options = new BuildOptions();
        StepRegistry = new StepRegistry();
        Logger = logger;

        // Default to local process execution.
        // Will be replaced with ContainerExecutor when Docker mode is active.
        Executor = new ProcessRunner(logger);

        // Operations receive an executor factory lambda so they always get
        // the current executor, even if it's changed after script loading.
        Dotnet = new DotnetOperations(StepRegistry, logger, () => Executor);
        Ef = new EfOperations(StepRegistry, logger, () => Executor);
        Npm = new NpmOperations(StepRegistry, logger, () => Executor);
        Azure = new AzureOperations(StepRegistry, logger, () => Executor);
        Bicep = new BicepOperations(StepRegistry, logger, () => Executor, Context.Vars);
        Cloudflare = new CloudflareOperations(StepRegistry, logger, () => Executor);
        Artifacts = new ArtifactOperations(logger);
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
