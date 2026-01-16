// =============================================================================
// ScriptGlobalsTests.cs
//
// Summary: Unit tests for ScriptGlobals class.
//
// Tests verify that:
// - All properties are correctly initialized from BuildContext
// - Dotnet.Project() creates correct ProjectRef instances
// - Directory() creates correct DirectoryRef instances
// - Env() correctly retrieves environment variables
// =============================================================================

using Ando.Operations;
using Ando.References;
using Ando.Scripting;
using Ando.Tests.TestFixtures;

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

        globals.Root.Value.ShouldNotBeNullOrEmpty();
        globals.Dotnet.ShouldNotBeNull();
        globals.Ef.ShouldNotBeNull();
        globals.Npm.ShouldNotBeNull();
        globals.Ando.ShouldNotBeNull();
    }

    [Fact]
    public void Root_ReturnsProjectRoot()
    {
        var globals = CreateGlobals("/my/project/root");

        globals.Root.Value.ShouldBe("/my/project/root");
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
    public void Ando_ReturnsAndoOperations()
    {
        var globals = CreateGlobals();

        globals.Ando.ShouldNotBeNull();
        globals.Ando.ShouldBeOfType<AndoOperations>();
    }

    [Fact]
    public void Dotnet_Project_CreatesProjectRef()
    {
        var globals = CreateGlobals();

        var project = globals.Dotnet.Project("./src/MyApp/MyApp.csproj");

        project.ShouldNotBeNull();
        project.ShouldBeOfType<ProjectRef>();
        project.Path.ShouldBe("./src/MyApp/MyApp.csproj");
    }

    [Fact]
    public void Dotnet_Project_ExtractsProjectName()
    {
        var globals = CreateGlobals();

        var project = globals.Dotnet.Project("./src/MyWebApp/MyWebApp.csproj");

        project.Name.ShouldBe("MyWebApp");
    }

    [Fact]
    public void Directory_CreatesDirectoryRef()
    {
        var globals = CreateGlobals();

        var dir = globals.Directory("./frontend");

        dir.ShouldNotBeNull();
        dir.ShouldBeOfType<DirectoryRef>();
        dir.Path.ShouldBe("./frontend");
    }

    [Fact]
    public void Directory_ExtractsDirectoryName()
    {
        var globals = CreateGlobals();

        var dir = globals.Directory("./path/to/frontend");

        dir.Name.ShouldBe("frontend");
    }

    [Fact]
    public void Directory_DefaultsToCurrentDirectory()
    {
        var globals = CreateGlobals();

        var dir = globals.Directory();

        dir.ShouldNotBeNull();
        dir.Path.ShouldBe(".");
    }

    [Fact]
    public void Env_ReturnsEnvironmentVariable()
    {
        var globals = CreateGlobals();
        Environment.SetEnvironmentVariable("TEST_SCRIPT_GLOBALS_VAR", "test-value");

        try
        {
            var value = globals.Env("TEST_SCRIPT_GLOBALS_VAR");

            value.ShouldBe("test-value");
        }
        finally
        {
            Environment.SetEnvironmentVariable("TEST_SCRIPT_GLOBALS_VAR", null);
        }
    }

    [Fact]
    public void Env_ThrowsWhenRequiredVariableNotSet()
    {
        var globals = CreateGlobals();

        Should.Throw<InvalidOperationException>(() => globals.Env("NONEXISTENT_TEST_VAR_12345"));
    }

    [Fact]
    public void Env_ReturnsNullWhenNotRequiredAndNotSet()
    {
        var globals = CreateGlobals();

        var value = globals.Env("NONEXISTENT_TEST_VAR_12345", required: false);

        value.ShouldBeNull();
    }
}
