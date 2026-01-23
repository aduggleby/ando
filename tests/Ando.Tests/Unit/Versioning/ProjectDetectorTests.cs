// =============================================================================
// ProjectDetectorTests.cs
//
// Unit tests for ProjectDetector parsing of build.csando files.
// =============================================================================

using Ando.Versioning;
using Shouldly;
using static Ando.Versioning.ProjectDetector;

namespace Ando.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class ProjectDetectorTests : IDisposable
{
    private readonly string _testDir;
    private readonly ProjectDetector _detector;

    public ProjectDetectorTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"project-detector-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _detector = new ProjectDetector();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    private string CreateBuildScript(string content)
    {
        var path = Path.Combine(_testDir, "build.csando");
        File.WriteAllText(path, content);
        return path;
    }

    private void CreateCsproj(string relativePath)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "<Project><PropertyGroup><Version>1.0.0</Version></PropertyGroup></Project>");
    }

    private void CreatePackageJson(string relativePath)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        File.WriteAllText(fullPath, "{\"name\":\"test\",\"version\":\"1.0.0\"}");
    }

    [Fact]
    public void DetectProjects_DotnetProject_Simple()
    {
        CreateCsproj("src/App/App.csproj");
        var script = CreateBuildScript(@"var app = Dotnet.Project(""./src/App/App.csproj"");");

        var projects = _detector.DetectProjects(script);

        projects.ShouldHaveSingleItem();
        projects[0].Path.ShouldBe("./src/App/App.csproj");
        projects[0].Type.ShouldBe(ProjectType.Dotnet);
    }

    [Fact]
    public void DetectProjects_DotnetProject_SingleQuotes()
    {
        CreateCsproj("src/App/App.csproj");
        var script = CreateBuildScript(@"var app = Dotnet.Project('./src/App/App.csproj');");

        var projects = _detector.DetectProjects(script);

        projects.ShouldHaveSingleItem();
        projects[0].Path.ShouldBe("./src/App/App.csproj");
    }

    [Fact]
    public void DetectProjects_DotnetProject_Inline()
    {
        CreateCsproj("src/App/App.csproj");
        var script = CreateBuildScript(@"Dotnet.Build(Dotnet.Project(""./src/App/App.csproj""));");

        var projects = _detector.DetectProjects(script);

        projects.ShouldHaveSingleItem();
    }

    [Fact]
    public void DetectProjects_MultipleDotnetProjects()
    {
        CreateCsproj("src/App/App.csproj");
        CreateCsproj("tests/App.Tests/App.Tests.csproj");
        var script = CreateBuildScript(@"
            var app = Dotnet.Project(""./src/App/App.csproj"");
            var tests = Dotnet.Project(""./tests/App.Tests/App.Tests.csproj"");
        ");

        var projects = _detector.DetectProjects(script);

        projects.Count.ShouldBe(2);
    }

    [Fact]
    public void DetectProjects_NpmProject_WithVariable()
    {
        CreatePackageJson("frontend/package.json");
        var script = CreateBuildScript(@"
            var frontend = Directory(""./frontend"");
            Npm.Ci(frontend);
            Npm.Build(frontend);
        ");

        var projects = _detector.DetectProjects(script);

        projects.ShouldHaveSingleItem();
        projects[0].Path.ShouldBe("./frontend/package.json");
        projects[0].Type.ShouldBe(ProjectType.Npm);
    }

    [Fact]
    public void DetectProjects_NpmProject_Inline()
    {
        CreatePackageJson("website/package.json");
        var script = CreateBuildScript(@"Npm.Install(Directory(""./website""));");

        var projects = _detector.DetectProjects(script);

        projects.ShouldHaveSingleItem();
        projects[0].Type.ShouldBe(ProjectType.Npm);
    }

    [Fact]
    public void DetectProjects_MixedProjects()
    {
        CreateCsproj("src/Api/Api.csproj");
        CreatePackageJson("frontend/package.json");
        var script = CreateBuildScript(@"
            var api = Dotnet.Project(""./src/Api/Api.csproj"");
            var frontend = Directory(""./frontend"");
            Dotnet.Build(api);
            Npm.Ci(frontend);
        ");

        var projects = _detector.DetectProjects(script);

        projects.Count.ShouldBe(2);
        projects.ShouldContain(p => p.Type == ProjectType.Dotnet);
        projects.ShouldContain(p => p.Type == ProjectType.Npm);
    }

    [Fact]
    public void DetectProjects_IgnoresNonExistentFiles()
    {
        // Don't create the csproj file.
        var script = CreateBuildScript(@"var app = Dotnet.Project(""./nonexistent.csproj"");");

        var projects = _detector.DetectProjects(script);

        projects.ShouldBeEmpty();
    }

    [Fact]
    public void DetectProjects_DirectoryNotUsedWithNpm_Ignored()
    {
        // Directory used without Npm operations shouldn't be detected.
        CreatePackageJson("data/package.json");
        var script = CreateBuildScript(@"
            var data = Directory(""./data"");
            // Just using directory for something else, no Npm.* calls.
        ");

        var projects = _detector.DetectProjects(script);

        projects.ShouldBeEmpty();
    }

    [Fact]
    public void DetectProjects_NoScript_ThrowsFileNotFound()
    {
        Should.Throw<FileNotFoundException>(() =>
            _detector.DetectProjects("/nonexistent/build.csando"));
    }
}
