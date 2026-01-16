// =============================================================================
// ScriptGlobals.cs
//
// Summary: Global variables exposed to build.csando scripts.
//
// ScriptGlobals defines what's available as global variables in build scripts.
// This is the "API surface" that script authors interact with. All properties
// here are accessible without qualification in scripts.
//
// Example build.csando script:
//   var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
//   var frontend = Directory("./frontend");
//   Dotnet.Restore(project);
//   Dotnet.Build(project);
//   Npm.Ci(frontend);
//   Npm.Run(frontend, "build");
//
// Design Decisions:
// - Properties are exposed directly as globals (no "Ando." prefix needed)
// - Root and Temp are top-level path globals for common directories
// - Directory is a function that creates typed references
// - Operations (Dotnet, Ef, Npm) register steps when called
// - Users use regular C# variables instead of a Vars dictionary
// =============================================================================

using Ando.Context;
using Ando.Operations;
using Ando.References;

namespace Ando.Scripting;

/// <summary>
/// Global variables exposed to build.csando scripts.
/// All properties are accessible as globals in script code.
/// </summary>
public class ScriptGlobals
{
    /// <summary>
    /// Project root directory (where build.csando is located). Allows: Root / "dist"
    /// </summary>
    public BuildPath Root { get; }

    /// <summary>
    /// Temporary files directory (root/.ando/tmp). Allows: Temp / "cache"
    /// </summary>
    public BuildPath Temp { get; }

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
    /// Usage: var deployment = Bicep.DeployToResourceGroup("rg", "./main.bicep");
    /// Access outputs: deployment.Output("sqlConnectionString")
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
    /// Node.js installation operations (installs Node.js globally).
    /// Usage: Node.Install() or Node.Install("20")
    /// </summary>
    public NodeInstallOperations Node { get; }

    /// <summary>
    /// Logging operations for build script output.
    /// Usage: Log.Info("message"), Log.Warning("message"), Log.Error("message"), Log.Debug("message")
    /// </summary>
    public LogOperations Log { get; }

    /// <summary>
    /// NuGet package operations (pack and push).
    /// Usage: Nuget.Pack(project, o => o.WithConfiguration(Configuration.Release))
    /// Usage: Nuget.Push("./pkg/MyPackage.1.0.0.nupkg", o => o.ToNuGetOrg().WithApiKey(apiKey))
    /// </summary>
    public NugetOperations Nuget { get; }

    /// <summary>
    /// ANDO operations for nested builds.
    /// Usage: Ando.Build(Directory("./website"))
    /// Usage: Ando.Build(Directory("./website"), o => o.WithDind())
    /// </summary>
    public AndoOperations Ando { get; }

    /// <summary>
    /// Creates a directory reference from a path.
    /// Usage: var frontend = Directory("./frontend");
    /// Usage: var current = Directory(); // defaults to "."
    /// </summary>
    public DirectoryRef Directory(string path = ".") => new DirectoryRef(path);

    /// <summary>
    /// Gets an environment variable value.
    /// By default, throws if the variable is not set. Pass required: false to return null instead.
    /// Usage: var apiKey = Env("API_KEY");
    /// Usage: var optional = Env("OPTIONAL_VAR", required: false);
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <param name="required">If true (default), throws when variable is not set. If false, returns null.</param>
    /// <returns>The environment variable value, or null if not set and required is false.</returns>
    public string? Env(string name, bool required = true)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (required && string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
        }

        return value;
    }

    public ScriptGlobals(BuildContext buildContext)
    {
        Root = buildContext.Context.Paths.Root;
        Temp = buildContext.Context.Paths.Temp;
        Dotnet = buildContext.Dotnet;
        Ef = buildContext.Ef;
        Npm = buildContext.Npm;
        Azure = buildContext.Azure;
        Bicep = buildContext.Bicep;
        Cloudflare = buildContext.Cloudflare;
        Functions = buildContext.Functions;
        AppService = buildContext.AppService;
        Node = buildContext.Node;
        Log = buildContext.Log;
        Nuget = buildContext.Nuget;
        Ando = buildContext.Ando;
    }
}
