// =============================================================================
// ProjectRefTests.cs
//
// Summary: Unit tests for ProjectRef class.
//
// Tests verify project reference creation, path handling, name extraction,
// and version reading from .csproj file paths.
// =============================================================================

using Ando.References;

namespace Ando.Tests.Unit.References;

[Trait("Category", "Unit")]
public class ProjectRefTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectRefTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ando-test-{Guid.NewGuid()}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

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

    [Fact]
    public void Version_ExtractsVersionFromCsproj()
    {
        // Create a temporary .csproj file with a version.
        var csprojPath = Path.Combine(_tempDir, "TestProject.csproj");
        File.WriteAllText(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <Version>1.2.3</Version>
  </PropertyGroup>
</Project>");

        var project = ProjectRef.From(csprojPath);

        Assert.Equal("1.2.3", project.Version);
    }

    [Fact]
    public void Version_ReturnsDefaultWhenNoVersionElement()
    {
        // Create a .csproj without a Version element.
        var csprojPath = Path.Combine(_tempDir, "NoVersion.csproj");
        File.WriteAllText(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>");

        var project = ProjectRef.From(csprojPath);

        Assert.Equal("0.0.0", project.Version);
    }

    [Fact]
    public void Version_ReturnsDefaultWhenFileDoesNotExist()
    {
        var project = ProjectRef.From("/nonexistent/path/MyApp.csproj");

        Assert.Equal("0.0.0", project.Version);
    }

    [Fact]
    public void Version_ReturnsDefaultForNonCsprojFiles()
    {
        // Create a package.json file.
        var packagePath = Path.Combine(_tempDir, "package.json");
        File.WriteAllText(packagePath, @"{ ""name"": ""test"", ""version"": ""2.0.0"" }");

        var project = ProjectRef.From(packagePath);

        Assert.Equal("0.0.0", project.Version);
    }

    [Fact]
    public void Version_HandlesPrereleaseSuffix()
    {
        var csprojPath = Path.Combine(_tempDir, "Prerelease.csproj");
        File.WriteAllText(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <Version>1.0.0-preview.1</Version>
  </PropertyGroup>
</Project>");

        var project = ProjectRef.From(csprojPath);

        Assert.Equal("1.0.0-preview.1", project.Version);
    }

    [Fact]
    public void Version_IsCached()
    {
        var csprojPath = Path.Combine(_tempDir, "Cached.csproj");
        File.WriteAllText(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <Version>1.0.0</Version>
  </PropertyGroup>
</Project>");

        var project = ProjectRef.From(csprojPath);

        // First access.
        var version1 = project.Version;

        // Modify the file.
        File.WriteAllText(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <Version>2.0.0</Version>
  </PropertyGroup>
</Project>");

        // Second access should return cached value.
        var version2 = project.Version;

        Assert.Equal("1.0.0", version1);
        Assert.Equal("1.0.0", version2);
    }

    [Fact]
    public void Version_TrimsWhitespace()
    {
        var csprojPath = Path.Combine(_tempDir, "Whitespace.csproj");
        File.WriteAllText(csprojPath, @"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <Version>  1.0.0  </Version>
  </PropertyGroup>
</Project>");

        var project = ProjectRef.From(csprojPath);

        Assert.Equal("1.0.0", project.Version);
    }
}
