// =============================================================================
// BumpCommandTests.cs
//
// Unit tests for BumpCommand version bumping logic.
// =============================================================================

using Ando.Cli.Commands;
using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Ando.Versioning;
using Shouldly;

namespace Ando.Tests.Unit.Cli.Commands;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Unit")]
public class BumpCommandTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _originalDir;
    private readonly TestLogger _logger;
    private readonly MockCliProcessRunner _runner;

    public BumpCommandTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"bump-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_testDir);

        _logger = new TestLogger();
        _runner = new MockCliProcessRunner();
    }

    public void Dispose()
    {
        try
        {
            Directory.SetCurrentDirectory(_originalDir);
        }
        catch (DirectoryNotFoundException)
        {
            // Original directory no longer exists (e.g., another test deleted it).
        }

        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    #region Build Script Detection Tests

    [Fact]
    public async Task ExecuteAsync_NoBuildScript_ReturnsError()
    {
        // No build.csando file created.
        var command = new BumpCommand(_runner, _logger);

        var result = await command.ExecuteAsync();

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("No build.csando found"));
    }

    [Fact]
    public async Task ExecuteAsync_NoBuildScript_ShowsHelpfulMessage()
    {
        var command = new BumpCommand(_runner, _logger);

        await command.ExecuteAsync();

        _logger.ErrorMessages.ShouldContain(m => m.Contains("build.csando"));
    }

    #endregion

    #region Project Detection Tests

    [Fact]
    public async Task ExecuteAsync_NoProjectsInBuildScript_ReturnsError()
    {
        CreateBuildScript(@"
            // Empty build script with no projects
            Log.Info(""Hello"");
        ");
        SetupCleanGitRepo();

        var command = new BumpCommand(_runner, _logger);
        var result = await command.ExecuteAsync();

        result.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("No projects found"));
    }

    [Fact]
    public async Task ExecuteAsync_DetectsDotnetProjects()
    {
        CreateBuildScript(@"
            var app = Dotnet.Project(""./src/App/App.csproj"");
            Dotnet.Build(app);
        ");
        CreateCsproj("src/App/App.csproj", "1.0.0");
        SetupCleanGitRepo();

        var command = new BumpCommand(_runner, _logger);
        await command.ExecuteAsync(BumpType.Patch);

        _logger.InfoMessages.ShouldContain(m => m.Contains("Detecting projects..."));
    }

    #endregion

    #region Uncommitted Changes Tests

    [Fact]
    public async Task ExecuteAsync_UncommittedChanges_ShowsWarning()
    {
        CreateBuildScript(@"var app = Dotnet.Project(""./App.csproj"");");
        CreateCsproj("App.csproj", "1.0.0");

        // Git reports uncommitted changes.
        _runner.SetOutput("git", "status --porcelain", " M file.txt\n");

        var command = new BumpCommand(_runner, _logger);

        // With autoConfirm=false (default), user would be prompted.
        // We can verify the warning message was shown.
        await command.ExecuteAsync(BumpType.Patch);

        _logger.WarningMessages.ShouldContain(m => m.Contains("uncommitted changes"));
    }

    #endregion

    #region Version Calculation Tests

    [Theory]
    [InlineData("1.0.0", BumpType.Patch, "1.0.1")]
    [InlineData("1.0.0", BumpType.Minor, "1.1.0")]
    [InlineData("1.0.0", BumpType.Major, "2.0.0")]
    [InlineData("2.3.4", BumpType.Patch, "2.3.5")]
    [InlineData("2.3.4", BumpType.Minor, "2.4.0")]
    [InlineData("2.3.4", BumpType.Major, "3.0.0")]
    public void SemVer_Bump_CalculatesCorrectVersion(string current, BumpType type, string expected)
    {
        var semver = SemVer.Parse(current);
        var bumped = semver.Bump(type);

        bumped.ToString().ShouldBe(expected);
    }

    [Fact]
    public void SemVer_Bump_Patch_PreservesPrerelease()
    {
        var semver = SemVer.Parse("1.0.0-beta.1");
        var bumped = semver.Bump(BumpType.Patch);

        // Bumping patch removes prerelease tag.
        bumped.ToString().ShouldBe("1.0.1");
    }

    #endregion

    #region Helper Methods

    private void CreateBuildScript(string content)
    {
        File.WriteAllText(Path.Combine(_testDir, "build.csando"), content);
    }

    private void CreateCsproj(string relativePath, string version)
    {
        var fullPath = Path.Combine(_testDir, relativePath);
        var dir = Path.GetDirectoryName(fullPath);
        if (dir != null && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(fullPath, $@"<Project>
            <PropertyGroup>
                <TargetFramework>net9.0</TargetFramework>
                <Version>{version}</Version>
            </PropertyGroup>
        </Project>");
    }

    private void SetupCleanGitRepo()
    {
        // Simulate a clean git repo with no uncommitted changes.
        _runner.SetOutput("git", "status --porcelain", "");
        _runner.SetResult("git", "rev-parse --git-dir", new CliProcessRunner.ProcessResult(0, ".git", ""));
    }

    #endregion
}
