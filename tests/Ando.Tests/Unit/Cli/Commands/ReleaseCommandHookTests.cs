// =============================================================================
// ReleaseCommandHookTests.cs
//
// Regression tests for release/ship hook behavior.
// =============================================================================

using Ando.Cli.Commands;
using Ando.Tests.TestFixtures;
using Shouldly;

namespace Ando.Tests.Unit.Cli.Commands;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Unit")]
public class ReleaseCommandHookTests : IDisposable
{
    private const string HookMarker = "POST_RELEASE_HOOK_EXECUTED";

    private readonly string _testDir;
    private readonly string _originalDir;
    private readonly TestLogger _logger;
    private readonly MockCliProcessRunner _runner;

    public ReleaseCommandHookTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"release-hook-cmd-{Guid.NewGuid():N}");
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

    [Fact]
    public async Task ExecuteAsync_WhenReleaseStepFails_DoesNotRunPostHook()
    {
        SetupReleaseProject();
        SetupGitState();
        CreatePostReleaseHook();

        _runner.SetResult("ando", "run --dind --read-env", new(0, "", ""));
        _runner.SetResult("ando", "run -p publish --dind --read-env", new(1, "", "publish failed"));

        var command = new ReleaseCommand(_runner, _logger);
        var result = await command.ExecuteAsync(all: true, commandName: "release");

        result.ShouldBe(1);
        _logger.InfoMessages.ShouldNotContain(m => m.Contains(HookMarker));
    }

    [Fact]
    public async Task ExecuteAsync_WhenReleaseSucceeds_RunsPostHook()
    {
        SetupReleaseProject();
        SetupGitState();
        CreatePostReleaseHook();

        _runner.SetResult("ando", "run --dind --read-env", new(0, "", ""));
        _runner.SetResult("ando", "run -p publish --dind --read-env", new(0, "", ""));

        var command = new ReleaseCommand(_runner, _logger);
        var result = await command.ExecuteAsync(all: true, commandName: "release");

        result.ShouldBe(0);
        _logger.InfoMessages.ShouldContain(m => m.Contains(HookMarker));
    }

    private void SetupReleaseProject()
    {
        Directory.CreateDirectory(Path.Combine(_testDir, "src", "App"));
        File.WriteAllText(Path.Combine(_testDir, "src", "App", "App.csproj"), """
<Project>
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Version>0.9.99</Version>
  </PropertyGroup>
</Project>
""");

        File.WriteAllText(Path.Combine(_testDir, "build.csando"), """
var app = Dotnet.Project("./src/App/App.csproj");
var publish = DefineProfile("publish");
Dotnet.Build(app);
""");
    }

    private void SetupGitState()
    {
        _runner.SetResult("git", "rev-parse --git-dir", new(0, ".git", ""));
        _runner.SetResult("git", "branch --show-current", new(0, "main\n", ""));
        _runner.SetResult("git", "status --porcelain", new(0, "", ""));
        _runner.SetResult("git", "rev-parse --abbrev-ref @{upstream}", new(128, "", "no upstream"));
        _runner.SetResult("git", "describe --tags --abbrev=0", new(0, "v0.9.98\n", ""));
        _runner.SetResult("git", "log v0.9.98..HEAD --pretty=format:%s", new(0, "", ""));
        _runner.SetResult("git", "rev-parse --short HEAD", new(0, "abc1234\n", ""));
    }

    private void CreatePostReleaseHook()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);
        File.WriteAllText(Path.Combine(scriptsDir, "ando-post-release.csando"), $"""
Log.Info("{HookMarker}");
""");
    }
}
