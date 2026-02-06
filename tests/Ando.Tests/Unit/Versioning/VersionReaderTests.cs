// =============================================================================
// VersionReaderTests.cs
//
// Unit tests for VersionReader version extraction from project files.
// =============================================================================

using Ando.Versioning;
using Shouldly;

namespace Ando.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class VersionReaderTests : IDisposable
{
    private readonly string _testDir;
    private readonly VersionReader _reader;

    public VersionReaderTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"version-reader-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _reader = new VersionReader();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    #region Csproj Tests

    [Fact]
    public void ReadVersion_Csproj_ReturnsVersion()
    {
        var csproj = CreateCsproj("1.2.3");

        var version = _reader.ReadVersion(csproj, ProjectDetector.ProjectType.Dotnet);

        version.ShouldBe("1.2.3");
    }

    [Fact]
    public void ReadVersion_Csproj_WithPrerelease_ReturnsVersion()
    {
        var csproj = CreateCsproj("1.2.3-beta.1");

        var version = _reader.ReadVersion(csproj, ProjectDetector.ProjectType.Dotnet);

        version.ShouldBe("1.2.3-beta.1");
    }

    [Fact]
    public void ReadVersion_Csproj_NoVersion_ReturnsNull()
    {
        var csproj = Path.Combine(_testDir, "no-version.csproj");
        File.WriteAllText(csproj, @"<Project>
            <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
            </PropertyGroup>
        </Project>");

        var version = _reader.ReadVersion(csproj, ProjectDetector.ProjectType.Dotnet);

        version.ShouldBeNull();
    }

    [Fact]
    public void ReadVersion_Csproj_MissingFile_ReturnsNull()
    {
        var csproj = Path.Combine(_testDir, "nonexistent.csproj");

        var version = _reader.ReadVersion(csproj, ProjectDetector.ProjectType.Dotnet);

        version.ShouldBeNull();
    }

    [Fact]
    public void ReadVersion_Csproj_InvalidXml_ReturnsNull()
    {
        var csproj = Path.Combine(_testDir, "invalid.csproj");
        File.WriteAllText(csproj, "this is not xml");

        var version = _reader.ReadVersion(csproj, ProjectDetector.ProjectType.Dotnet);

        version.ShouldBeNull();
    }

    [Fact]
    public void ReadVersion_Csproj_VersionInConditionalPropertyGroup_ReturnsVersion()
    {
        var csproj = Path.Combine(_testDir, "conditional.csproj");
        File.WriteAllText(csproj, @"<Project>
            <PropertyGroup Condition=""'$(Configuration)' == 'Release'"">
                <Version>2.0.0</Version>
            </PropertyGroup>
        </Project>");

        var version = _reader.ReadVersion(csproj, ProjectDetector.ProjectType.Dotnet);

        version.ShouldBe("2.0.0");
    }

    [Fact]
    public void ReadVersion_Csproj_InvalidVersion_Throws()
    {
        var csproj = Path.Combine(_testDir, "invalid-version.csproj");
        File.WriteAllText(csproj, @"<Project>
            <PropertyGroup>
                <Version>not-a-version</Version>
            </PropertyGroup>
        </Project>");

        Should.Throw<InvalidOperationException>(() =>
            _reader.ReadVersion(csproj, ProjectDetector.ProjectType.Dotnet));
    }

    #endregion

    #region Package.json Tests

    [Fact]
    public void ReadVersion_PackageJson_ReturnsVersion()
    {
        var packageJson = CreatePackageJson("3.4.5");

        var version = _reader.ReadVersion(packageJson, ProjectDetector.ProjectType.Npm);

        version.ShouldBe("3.4.5");
    }

    [Fact]
    public void ReadVersion_PackageJson_WithPrerelease_ReturnsVersion()
    {
        var packageJson = CreatePackageJson("1.0.0-alpha.2");

        var version = _reader.ReadVersion(packageJson, ProjectDetector.ProjectType.Npm);

        version.ShouldBe("1.0.0-alpha.2");
    }

    [Fact]
    public void ReadVersion_PackageJson_NoVersion_ReturnsNull()
    {
        var packageJson = Path.Combine(_testDir, "no-version.json");
        File.WriteAllText(packageJson, @"{ ""name"": ""test-package"" }");

        var version = _reader.ReadVersion(packageJson, ProjectDetector.ProjectType.Npm);

        version.ShouldBeNull();
    }

    [Fact]
    public void ReadVersion_PackageJson_MissingFile_ReturnsNull()
    {
        var packageJson = Path.Combine(_testDir, "nonexistent.json");

        var version = _reader.ReadVersion(packageJson, ProjectDetector.ProjectType.Npm);

        version.ShouldBeNull();
    }

    [Fact]
    public void ReadVersion_PackageJson_InvalidJson_ReturnsNull()
    {
        var packageJson = Path.Combine(_testDir, "invalid.json");
        File.WriteAllText(packageJson, "not json");

        var version = _reader.ReadVersion(packageJson, ProjectDetector.ProjectType.Npm);

        version.ShouldBeNull();
    }

    [Fact]
    public void ReadVersion_PackageJson_InvalidVersion_Throws()
    {
        var packageJson = Path.Combine(_testDir, "invalid-version.json");
        File.WriteAllText(packageJson, @"{ ""version"": ""invalid"" }");

        Should.Throw<InvalidOperationException>(() =>
            _reader.ReadVersion(packageJson, ProjectDetector.ProjectType.Npm));
    }

    #endregion

    #region Helpers

    private string CreateCsproj(string version)
    {
        var path = Path.Combine(_testDir, $"test-{Guid.NewGuid():N}.csproj");
        File.WriteAllText(path, $@"<Project>
            <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <Version>{version}</Version>
            </PropertyGroup>
        </Project>");
        return path;
    }

    private string CreatePackageJson(string version)
    {
        var path = Path.Combine(_testDir, $"package-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $@"{{ ""name"": ""test"", ""version"": ""{version}"" }}");
        return path;
    }

    #endregion
}
