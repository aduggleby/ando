// =============================================================================
// ReleaseCommandTests.cs
//
// Unit tests for ReleaseCommand helper methods.
// =============================================================================

using Ando.Cli.Commands;
using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Ando.Versioning;
using Shouldly;

namespace Ando.Tests.Unit.Release;

[Trait("Category", "Unit")]
public class ReleaseCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly TestLogger _logger;

    public ReleaseCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"release-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _logger = new TestLogger();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void HasPublishProfile_DetectsDefineProfile()
    {
        var buildScript = Path.Combine(_testDir, "build.csando");
        File.WriteAllText(buildScript, @"
            var publish = DefineProfile(""publish"");
            Dotnet.Build(app);
        ");

        var content = File.ReadAllText(buildScript);
        var hasPublish = content.Contains("DefineProfile(\"publish\"") ||
                         content.Contains("DefineProfile('publish'");

        hasPublish.ShouldBeTrue();
    }

    [Fact]
    public void HasPublishProfile_ReturnsFalse_WhenNoPublishProfile()
    {
        var buildScript = Path.Combine(_testDir, "build.csando");
        File.WriteAllText(buildScript, @"
            var release = DefineProfile(""release"");
            Dotnet.Build(app);
        ");

        var content = File.ReadAllText(buildScript);
        var hasPublish = content.Contains("DefineProfile(\"publish\"") ||
                         content.Contains("DefineProfile('publish'");

        hasPublish.ShouldBeFalse();
    }

    [Fact]
    public void GetCurrentVersion_ReadsFromFirstProject()
    {
        // Create build script and project.
        var buildScript = Path.Combine(_testDir, "build.csando");
        File.WriteAllText(buildScript, @"
            var app = Dotnet.Project(""./src/App/App.csproj"");
        ");

        var projectDir = Path.Combine(_testDir, "src", "App");
        Directory.CreateDirectory(projectDir);
        var csproj = Path.Combine(projectDir, "App.csproj");
        File.WriteAllText(csproj, @"<Project>
            <PropertyGroup>
                <Version>1.2.3</Version>
            </PropertyGroup>
        </Project>");

        var detector = new ProjectDetector();
        var projects = detector.DetectProjects(buildScript);
        projects.ShouldHaveSingleItem();

        var reader = new VersionReader();
        var fullPath = Path.Combine(_testDir, projects[0].Path);
        var version = reader.ReadVersion(fullPath, projects[0].Type);

        version.ShouldBe("1.2.3");
    }

    [Fact]
    public void CalculateNextVersion_Patch()
    {
        if (!SemVer.TryParse("1.2.3", out var semver))
            throw new Exception("Failed to parse version");

        var next = semver.Bump(BumpType.Patch).ToString();

        next.ShouldBe("1.2.4");
    }

    [Fact]
    public void CalculateNextVersion_Minor()
    {
        if (!SemVer.TryParse("1.2.3", out var semver))
            throw new Exception("Failed to parse version");

        var next = semver.Bump(BumpType.Minor).ToString();

        next.ShouldBe("1.3.0");
    }

    [Fact]
    public void CalculateNextVersion_Major()
    {
        if (!SemVer.TryParse("1.2.3", out var semver))
            throw new Exception("Failed to parse version");

        var next = semver.Bump(BumpType.Major).ToString();

        next.ShouldBe("2.0.0");
    }
}
