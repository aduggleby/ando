// =============================================================================
// BuildOperations.cs
//
// Summary: Container for all build operation instances.
//
// BuildOperations holds all the operation classes that provide the ANDO API
// for build scripts. This separates operation instances from build state/config
// in BuildContext, following the Single Responsibility Principle.
//
// Architecture:
// - Operations receive an executor factory to get the current executor
// - Executor can be switched from ProcessRunner to ContainerExecutor after script load
// - Each operation class registers steps in the shared StepRegistry
//
// Design Decisions:
// - Extracted from BuildContext to improve separation of concerns
// - All operations created in constructor for consistency and discoverability
// - Factory pattern for executor supports swapping execution strategy
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Operations;
using Ando.Steps;
using Ando.Workflow;

namespace Ando.Scripting;

/// <summary>
/// Container for all build operation instances.
/// Provides access to all operation classes (Dotnet, Npm, Azure, etc.) that
/// build scripts use to register build steps.
/// </summary>
public class BuildOperations
{
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

    /// <summary>Azure Functions deployment operations.</summary>
    public FunctionsOperations Functions { get; }

    /// <summary>Azure App Service deployment operations.</summary>
    public AppServiceOperations AppService { get; }

    /// <summary>Artifact operations for copying files to host (internal use).</summary>
    internal ArtifactOperations Artifacts { get; }

    /// <summary>Node.js installation operations (installs Node.js globally).</summary>
    public NodeInstallOperations Node { get; }

    /// <summary>Logging operations for build script output.</summary>
    public LogOperations Log { get; }

    /// <summary>NuGet package operations (pack, push).</summary>
    public NugetOperations Nuget { get; }

    /// <summary>ANDO operations for image configuration, artifacts, and nested builds.</summary>
    public AndoOperations Ando { get; }

    /// <summary>
    /// Creates all operation instances.
    /// </summary>
    /// <param name="registry">Step registry for operations to register steps.</param>
    /// <param name="logger">Logger for operations to use.</param>
    /// <param name="executorFactory">Factory to get current executor.</param>
    /// <param name="containerToHostPath">Function to translate container paths to host paths.</param>
    /// <param name="buildOptions">Build options for configuring the current build.</param>
    public BuildOperations(
        StepRegistry registry,
        IBuildLogger logger,
        Func<ICommandExecutor> executorFactory,
        Func<string, string> containerToHostPath,
        BuildOptions buildOptions)
    {
        Dotnet = new DotnetOperations(registry, logger, executorFactory);
        Ef = new EfOperations(registry, logger, executorFactory);
        Npm = new NpmOperations(registry, logger, executorFactory);
        Azure = new AzureOperations(registry, logger, executorFactory);
        Bicep = new BicepOperations(registry, logger, executorFactory);
        Cloudflare = new CloudflareOperations(registry, logger, executorFactory);
        Functions = new FunctionsOperations(registry, logger, executorFactory);
        AppService = new AppServiceOperations(registry, logger, executorFactory);
        Artifacts = new ArtifactOperations(logger);
        Node = new NodeInstallOperations(registry, logger, executorFactory);
        Log = new LogOperations(registry);
        Nuget = new NugetOperations(registry, logger, executorFactory);
        Ando = new AndoOperations(registry, logger, containerToHostPath, buildOptions, Artifacts);
    }
}
