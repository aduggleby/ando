// =============================================================================
// ReleaseCommandHookTests.cs
//
// Regression tests for release/ship hook behavior.
//
// These tests redirect Console.Out and reset Spectre.Console's cached writer
// because ReleaseCommand writes to AnsiConsole/Console directly. Without this,
// parallel xUnit test execution can close the shared Console.Out writer, causing
// intermittent "Cannot write to a closed TextWriter" failures.
// =============================================================================

using System.Reflection;
using Ando.Cli.Commands;
using Ando.Tests.TestFixtures;
using Shouldly;
using Spectre.Console;

namespace Ando.Tests.Unit.Cli.Commands;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Unit")]
public class ReleaseCommandHookTests : IDisposable
{
    private const string HookMarker = "POST_RELEASE_HOOK_EXECUTED";

    private readonly string _testDir;
    private readonly string _originalDir;
    private readonly TextWriter _originalOut;
    private readonly TextWriter _originalError;
    private readonly TestLogger _logger;
    private readonly MockCliProcessRunner _runner;

    public ReleaseCommandHookTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"release-hook-cmd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _originalDir = Directory.GetCurrentDirectory();
        Directory.SetCurrentDirectory(_testDir);

        // Redirect Console.Out/Error to avoid "Cannot write to a closed TextWriter"
        // when xUnit's parallel test runner closes the shared console writer.
        _originalOut = Console.Out;
        _originalError = Console.Error;
        Console.SetOut(new StringWriter());
        Console.SetError(new StringWriter());

        // Reset Spectre.Console's cached console so it picks up the redirected writer.
        // AnsiConsole caches a Lazy<IAnsiConsole> that holds a reference to Console.Out
        // at the time it was first used. Without this reset, it would use the old (potentially
        // closed) writer even after Console.SetOut.
        ResetAnsiConsole();

        _logger = new TestLogger();
        _runner = new MockCliProcessRunner();
    }

    public void Dispose()
    {
        Console.SetOut(_originalOut);
        Console.SetError(_originalError);
        ResetAnsiConsole();

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

        var errors = string.Join("; ", _logger.ErrorMessages);
        result.ShouldBe(0, $"Errors: [{errors}]");
        _logger.InfoMessages.ShouldContain(m => m.Contains(HookMarker));
    }

    /// <summary>
    /// Resets Spectre.Console's cached IAnsiConsole so it uses the current Console.Out.
    /// The IAnsiConsole is created eagerly to avoid a circular Lazy reference:
    /// AnsiConsole.Create() can access AnsiConsole properties backed by the same Lazy,
    /// causing "ValueFactory attempted to access the Value property of this instance".
    /// </summary>
    private static void ResetAnsiConsole()
    {
        var field = typeof(AnsiConsole).GetField("_console", BindingFlags.Static | BindingFlags.NonPublic);
        if (field != null)
        {
            var console = AnsiConsole.Create(new AnsiConsoleSettings());
            field.SetValue(null, new Lazy<IAnsiConsole>(() => console));
        }
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
