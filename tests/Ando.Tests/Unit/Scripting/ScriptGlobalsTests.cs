// =============================================================================
// ScriptGlobalsTests.cs
//
// Summary: Unit tests for ScriptGlobals class.
//
// Tests verify that:
// - All properties are correctly initialized from BuildContext
// - Project.From creates correct ProjectRef instances
// - Configuration exposes the correct value from Options
// =============================================================================

using Ando.Operations;
using Ando.References;
using Ando.Scripting;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Scripting;

[Trait("Category", "Unit")]
public class ScriptGlobalsTests
{
    private readonly TestLogger _logger = new();

    private ScriptGlobals CreateGlobals(string rootPath = "/test/root")
    {
        var buildContext = new BuildContext(rootPath, _logger);
        return new ScriptGlobals(buildContext);
    }

    [Fact]
    public void Constructor_InitializesAllProperties()
    {
        var globals = CreateGlobals();

        globals.Context.ShouldNotBeNull();
        globals.Options.ShouldNotBeNull();
        globals.Root.Value.ShouldNotBeNullOrEmpty(); // BuildPath is a struct
        globals.Dotnet.ShouldNotBeNull();
        globals.Ef.ShouldNotBeNull();
        globals.Npm.ShouldNotBeNull();
        globals.Artifacts.ShouldNotBeNull();
        globals.Project.ShouldNotBeNull();
    }

    [Fact]
    public void Context_ReturnsContextObject()
    {
        var globals = CreateGlobals("/my/project");

        globals.Context.ShouldNotBeNull();
        globals.Context.Paths.ShouldNotBeNull();
        globals.Context.Vars.ShouldNotBeNull();
    }

    [Fact]
    public void Options_ReturnsBuildOptions()
    {
        var globals = CreateGlobals();

        globals.Options.ShouldNotBeNull();
        globals.Options.ShouldBeOfType<BuildOptions>();
    }

    [Fact]
    public void Root_MatchesContextPathsRoot()
    {
        var globals = CreateGlobals("/my/project/root");

        globals.Root.Value.ShouldBe(globals.Context.Paths.Root.Value);
    }

    [Fact]
    public void Dotnet_ReturnsDotnetOperations()
    {
        var globals = CreateGlobals();

        globals.Dotnet.ShouldNotBeNull();
        globals.Dotnet.ShouldBeOfType<DotnetOperations>();
    }

    [Fact]
    public void Ef_ReturnsEfOperations()
    {
        var globals = CreateGlobals();

        globals.Ef.ShouldNotBeNull();
        globals.Ef.ShouldBeOfType<EfOperations>();
    }

    [Fact]
    public void Npm_ReturnsNpmOperations()
    {
        var globals = CreateGlobals();

        globals.Npm.ShouldNotBeNull();
        globals.Npm.ShouldBeOfType<NpmOperations>();
    }

    [Fact]
    public void Artifacts_ReturnsArtifactOperations()
    {
        var globals = CreateGlobals();

        globals.Artifacts.ShouldNotBeNull();
        globals.Artifacts.ShouldBeOfType<ArtifactOperations>();
    }

    [Fact]
    public void Configuration_ReturnsOptionsConfiguration()
    {
        var globals = CreateGlobals();

        // Default configuration
        globals.Configuration.ShouldBe(Configuration.Debug);
    }

    [Fact]
    public void Configuration_ReflectsOptionsChanges()
    {
        var globals = CreateGlobals();

        globals.Options.UseConfiguration(Configuration.Release);

        globals.Configuration.ShouldBe(Configuration.Release);
    }

    [Fact]
    public void Project_From_CreatesProjectRef()
    {
        var globals = CreateGlobals();

        var project = globals.Project.From("./src/MyApp/MyApp.csproj");

        project.ShouldNotBeNull();
        project.ShouldBeOfType<ProjectRef>();
        project.Path.ShouldBe("./src/MyApp/MyApp.csproj");
    }

    [Fact]
    public void Project_From_ExtractsProjectName()
    {
        var globals = CreateGlobals();

        var project = globals.Project.From("./src/MyWebApp/MyWebApp.csproj");

        project.Name.ShouldBe("MyWebApp");
    }
}
