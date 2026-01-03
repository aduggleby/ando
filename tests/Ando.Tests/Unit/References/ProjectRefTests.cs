// =============================================================================
// ProjectRefTests.cs
//
// Summary: Unit tests for ProjectRef class.
//
// Tests verify project reference creation, path handling, and name extraction
// from .csproj file paths.
// =============================================================================

using Ando.References;

namespace Ando.Tests.Unit.References;

[Trait("Category", "Unit")]
public class ProjectRefTests
{
    [Fact]
    public void From_CreatesProjectRef()
    {
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        Assert.NotNull(project);
    }

    [Fact]
    public void Name_ExtractsProjectName()
    {
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        Assert.Equal("MyApp", project.Name);
    }

    [Fact]
    public void Path_PreservesPath()
    {
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        Assert.Equal("./src/MyApp/MyApp.csproj", project.Path);
    }

    [Fact]
    public void Directory_ExtractsDirectory()
    {
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        Assert.Equal("./src/MyApp", project.Directory);
    }

    [Fact]
    public void ToString_ReturnsName()
    {
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        Assert.Equal("MyApp", project.ToString());
    }

    [Fact]
    public void ImplicitConversion_ReturnsPath()
    {
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        string path = project;

        Assert.Equal("./src/MyApp/MyApp.csproj", path);
    }

    [Fact]
    public void From_WithAbsolutePath_ExtractsCorrectName()
    {
        var project = ProjectRef.From("/home/user/projects/MyProject/MyProject.csproj");

        Assert.Equal("MyProject", project.Name);
    }

    [Fact]
    public void From_WithDeepPath_ExtractsCorrectName()
    {
        var project = ProjectRef.From("./src/Services/Auth/Auth.Service/Auth.Service.csproj");

        Assert.Equal("Auth.Service", project.Name);
    }
}
