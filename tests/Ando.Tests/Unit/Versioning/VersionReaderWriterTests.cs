// =============================================================================
// VersionReaderWriterTests.cs
//
// Unit tests for VersionReader and VersionWriter functionality.
// =============================================================================

using Ando.Versioning;
using Shouldly;
using static Ando.Versioning.ProjectDetector;

namespace Ando.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class VersionReaderWriterTests : IDisposable
{
    private readonly string _testDir;
    private readonly VersionReader _reader;
    private readonly VersionWriter _writer;

    public VersionReaderWriterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"version-rw-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _reader = new VersionReader();
        _writer = new VersionWriter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    #region VersionReader Tests

    [Fact]
    public void ReadVersion_Csproj_ReadsVersion()
    {
        var path = CreateCsproj("1.2.3");
        var version = _reader.ReadVersion(path, ProjectType.Dotnet);

        version.ShouldBe("1.2.3");
    }

    [Fact]
    public void ReadVersion_Csproj_WithPrerelease()
    {
        var path = CreateCsproj("1.2.3-beta");
        var version = _reader.ReadVersion(path, ProjectType.Dotnet);

        version.ShouldBe("1.2.3-beta");
    }

    [Fact]
    public void ReadVersion_Csproj_NoVersion_ReturnsNull()
    {
        var path = Path.Combine(_testDir, "test.csproj");
        File.WriteAllText(path, "<Project><PropertyGroup></PropertyGroup></Project>");

        var version = _reader.ReadVersion(path, ProjectType.Dotnet);

        version.ShouldBeNull();
    }

    [Fact]
    public void ReadVersion_Csproj_InvalidVersion_ThrowsInvalidOperation()
    {
        var path = Path.Combine(_testDir, "test.csproj");
        File.WriteAllText(path, "<Project><PropertyGroup><Version>invalid</Version></PropertyGroup></Project>");

        Should.Throw<InvalidOperationException>(() =>
            _reader.ReadVersion(path, ProjectType.Dotnet));
    }

    [Fact]
    public void ReadVersion_PackageJson_ReadsVersion()
    {
        var path = CreatePackageJson("1.2.3");
        var version = _reader.ReadVersion(path, ProjectType.Npm);

        version.ShouldBe("1.2.3");
    }

    [Fact]
    public void ReadVersion_PackageJson_NoVersion_ReturnsNull()
    {
        var path = Path.Combine(_testDir, "package.json");
        File.WriteAllText(path, @"{""name"": ""test""}");

        var version = _reader.ReadVersion(path, ProjectType.Npm);

        version.ShouldBeNull();
    }

    [Fact]
    public void ReadVersion_NonExistentFile_ReturnsNull()
    {
        var version = _reader.ReadVersion("/nonexistent.csproj", ProjectType.Dotnet);

        version.ShouldBeNull();
    }

    #endregion

    #region VersionWriter Tests

    [Fact]
    public void WriteVersion_Csproj_UpdatesVersion()
    {
        var path = CreateCsproj("1.0.0");

        _writer.WriteVersion(path, ProjectType.Dotnet, "1.2.3");

        var content = File.ReadAllText(path);
        content.ShouldContain("<Version>1.2.3</Version>");
    }

    [Fact]
    public void WriteVersion_Csproj_PreservesOtherContent()
    {
        var path = Path.Combine(_testDir, "test.csproj");
        File.WriteAllText(path, @"<Project>
  <PropertyGroup>
    <Version>1.0.0</Version>
    <TargetFramework>net9.0</TargetFramework>
  </PropertyGroup>
</Project>");

        _writer.WriteVersion(path, ProjectType.Dotnet, "2.0.0");

        var content = File.ReadAllText(path);
        content.ShouldContain("<TargetFramework>net9.0</TargetFramework>");
        content.ShouldContain("<Version>2.0.0</Version>");
    }

    [Fact]
    public void WriteVersion_Csproj_NoVersionElement_ThrowsInvalidOperation()
    {
        var path = Path.Combine(_testDir, "test.csproj");
        File.WriteAllText(path, "<Project><PropertyGroup></PropertyGroup></Project>");

        Should.Throw<InvalidOperationException>(() =>
            _writer.WriteVersion(path, ProjectType.Dotnet, "1.0.0"));
    }

    [Fact]
    public void WriteVersion_InvalidVersion_ThrowsArgumentException()
    {
        var path = CreateCsproj("1.0.0");

        Should.Throw<ArgumentException>(() =>
            _writer.WriteVersion(path, ProjectType.Dotnet, "invalid"));
    }

    [Fact]
    public void WriteVersion_PackageJson_UpdatesVersion()
    {
        var path = CreatePackageJson("1.0.0");

        _writer.WriteVersion(path, ProjectType.Npm, "1.2.3");

        var content = File.ReadAllText(path);
        content.ShouldContain(@"""version"": ""1.2.3""");
    }

    [Fact]
    public void WriteVersion_PackageJson_PreservesFormatting()
    {
        var path = Path.Combine(_testDir, "package.json");
        File.WriteAllText(path, @"{
  ""name"": ""test"",
  ""version"": ""1.0.0"",
  ""description"": ""Test package""
}");

        _writer.WriteVersion(path, ProjectType.Npm, "2.0.0");

        var content = File.ReadAllText(path);
        content.ShouldContain(@"""name"": ""test""");
        content.ShouldContain(@"""description"": ""Test package""");
    }

    [Fact]
    public void WriteVersion_PackageJson_NoVersion_ThrowsInvalidOperation()
    {
        var path = Path.Combine(_testDir, "package.json");
        File.WriteAllText(path, @"{""name"": ""test""}");

        Should.Throw<InvalidOperationException>(() =>
            _writer.WriteVersion(path, ProjectType.Npm, "1.0.0"));
    }

    #endregion

    #region Helpers

    private string CreateCsproj(string version)
    {
        var path = Path.Combine(_testDir, $"test-{Guid.NewGuid():N}.csproj");
        File.WriteAllText(path, $"<Project><PropertyGroup><Version>{version}</Version></PropertyGroup></Project>");
        return path;
    }

    private string CreatePackageJson(string version)
    {
        var path = Path.Combine(_testDir, $"package-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, $@"{{""name"": ""test"", ""version"": ""{version}""}}");
        return path;
    }

    #endregion
}
