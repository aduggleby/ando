// =============================================================================
// ScriptGlobals.cs
//
// Summary: Global variables exposed to build.ando scripts.
//
// ScriptGlobals defines what's available as global variables in build scripts.
// This is the "API surface" that script authors interact with. All properties
// here are accessible without qualification in scripts.
//
// Example build.ando script:
//   var project = Project.From("./src/MyApp/MyApp.csproj");
//   Dotnet.Restore(project);
//   Dotnet.Build(project);
//   Dotnet.Publish(project, opt => opt
//       .WithConfiguration(Configuration.Release)
//       .Output(Context.Paths.Artifacts / "app"));
//
// Design Decisions:
// - Properties are exposed directly as globals (no "Ando." prefix needed)
// - Root is a shorthand for Context.Paths.Root to reduce verbosity
// - Project helper provides a natural syntax: Project.From(path)
// - Operations (Dotnet, Ef, Npm) register steps when called
// =============================================================================

using Ando.Context;
using Ando.Operations;
using Ando.References;
using Ando.Workflow;

namespace Ando.Scripting;

/// <summary>
/// Global variables exposed to build.ando scripts.
/// All properties are accessible as globals in script code.
/// </summary>
public class ScriptGlobals
{
    /// <summary>
    /// Unified context object with Paths and Vars properties.
    /// </summary>
    public BuildContextObject Context { get; }

    /// <summary>
    /// Build options (configuration, etc.).
    /// </summary>
    public BuildOptions Options { get; }

    /// <summary>
    /// Shorthand for Context.Paths.Root. Allows: Root / "dist"
    /// </summary>
    public BuildPath Root { get; }

    /// <summary>
    /// Dotnet CLI operations (build, test, publish, etc.).
    /// </summary>
    public DotnetOperations Dotnet { get; }

    /// <summary>
    /// Entity Framework operations (database update, migrations, etc.).
    /// </summary>
    public EfOperations Ef { get; }

    /// <summary>
    /// npm operations (install, run, ci, etc.).
    /// </summary>
    public NpmOperations Npm { get; }

    /// <summary>
    /// Azure CLI authentication and account operations.
    /// Usage: Azure.EnsureLoggedIn(), Azure.SetSubscription("...")
    /// </summary>
    public AzureOperations Azure { get; }

    /// <summary>
    /// Azure Bicep deployment operations.
    /// Usage: Bicep.DeployToResourceGroup("rg", "./main.bicep", o => o.CaptureOutputs())
    /// </summary>
    public BicepOperations Bicep { get; }

    /// <summary>
    /// Cloudflare operations (Pages deployment, etc.).
    /// Usage: Cloudflare.PagesDeploy(o => o.WithProjectName("my-site"))
    /// </summary>
    public CloudflareOperations Cloudflare { get; }

    /// <summary>
    /// Azure Functions deployment operations.
    /// Usage: Functions.DeployZip("my-func", "./publish.zip", o => o.WithDeploymentSlot("staging"))
    /// </summary>
    public FunctionsOperations Functions { get; }

    /// <summary>
    /// Azure App Service deployment operations.
    /// Usage: AppService.DeployZip("my-app", "./publish.zip", o => o.WithDeploymentSlot("staging"))
    /// </summary>
    public AppServiceOperations AppService { get; }

    /// <summary>
    /// Artifact operations for specifying which files to copy back to host.
    /// Usage: Artifacts.CopyToHost("/workspace/dist", "./dist")
    /// </summary>
    public ArtifactOperations Artifacts { get; }

    /// <summary>
    /// Helper to create project references.
    /// Usage: Project.From("path/to/project.csproj")
    /// </summary>
    public ProjectHelper Project { get; }

    /// <summary>
    /// Current build configuration (Debug or Release).
    /// </summary>
    public Configuration Configuration => Options.Configuration;

    public ScriptGlobals(BuildContext buildContext)
    {
        Context = buildContext.Context;
        Options = buildContext.Options;
        Root = buildContext.Context.Paths.Root;
        Dotnet = buildContext.Dotnet;
        Ef = buildContext.Ef;
        Npm = buildContext.Npm;
        Azure = buildContext.Azure;
        Bicep = buildContext.Bicep;
        Cloudflare = buildContext.Cloudflare;
        Functions = buildContext.Functions;
        AppService = buildContext.AppService;
        Artifacts = buildContext.Artifacts;
        Project = new ProjectHelper();
    }

    /// <summary>
    /// Helper class for creating project references.
    /// Provides natural syntax: Project.From("path")
    /// </summary>
    public class ProjectHelper
    {
        /// <summary>Creates a project reference from a path.</summary>
        public ProjectRef From(string path) => ProjectRef.From(path);
    }
}
