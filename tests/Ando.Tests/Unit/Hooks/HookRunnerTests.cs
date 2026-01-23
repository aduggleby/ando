// =============================================================================
// HookRunnerTests.cs
//
// Unit tests for HookRunner hook discovery and execution order.
// =============================================================================

using Ando.Hooks;
using Ando.Tests.TestFixtures;
using Shouldly;

namespace Ando.Tests.Unit.Hooks;

[Trait("Category", "Unit")]
public class HookRunnerTests : IDisposable
{
    private readonly string _testDir;
    private readonly TestLogger _logger;
    private readonly HookRunner _runner;

    public HookRunnerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"hook-runner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _logger = new TestLogger();
        _runner = new HookRunner(_testDir, _logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    #region Hook Discovery Tests

    [Fact]
    public async Task RunHooksAsync_FindsHooksInScriptsDirectory()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);
        CreateHookScript(Path.Combine(scriptsDir, "ando-pre-bump.csando"), "Log.Info(\"Found!\");");

        var context = new HookContext { Command = "bump" };
        await _runner.RunHooksAsync(HookRunner.HookType.Pre, "bump", context);

        _logger.InfoMessages.ShouldContain(m => m.Contains("Running hook: ando-pre-bump.csando"));
    }

    [Fact]
    public async Task RunHooksAsync_FindsHooksInRootDirectory()
    {
        CreateHookScript(Path.Combine(_testDir, "ando-pre-commit.csando"), "Log.Info(\"Root hook\");");

        var context = new HookContext { Command = "commit" };
        await _runner.RunHooksAsync(HookRunner.HookType.Pre, "commit", context);

        _logger.InfoMessages.ShouldContain(m => m.Contains("Running hook: ando-pre-commit.csando"));
    }

    [Fact]
    public async Task RunHooksAsync_PrefersScriptsDirectoryOverRoot()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        // Create both root and scripts versions.
        CreateHookScript(Path.Combine(_testDir, "ando-pre-bump.csando"), "Log.Info(\"root\");");
        CreateHookScript(Path.Combine(scriptsDir, "ando-pre-bump.csando"), "Log.Info(\"scripts\");");

        var context = new HookContext { Command = "bump" };
        await _runner.RunHooksAsync(HookRunner.HookType.Pre, "bump", context);

        // Should execute only the scripts version.
        _logger.InfoMessages.Count(m => m.Contains("Running hook:")).ShouldBe(1);
    }

    [Fact]
    public async Task RunHooksAsync_SkipsMissingHooks()
    {
        // No hooks created.
        var context = new HookContext { Command = "bump" };
        var result = await _runner.RunHooksAsync(HookRunner.HookType.Pre, "bump", context);

        result.ShouldBeTrue();
        _logger.InfoMessages.ShouldNotContain(m => m.Contains("Running hook:"));
    }

    #endregion

    #region Execution Order Tests

    [Fact]
    public async Task RunHooksAsync_RunsGeneralHookBeforeSpecific()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        // Order is tracked via info messages.
        CreateHookScript(Path.Combine(scriptsDir, "ando-pre.csando"), "Log.Info(\"general\");");
        CreateHookScript(Path.Combine(scriptsDir, "ando-pre-bump.csando"), "Log.Info(\"specific\");");

        var context = new HookContext { Command = "bump" };
        await _runner.RunHooksAsync(HookRunner.HookType.Pre, "bump", context);

        var runningMessages = _logger.InfoMessages.Where(m => m.Contains("Running hook:")).ToList();
        runningMessages.Count.ShouldBe(2);
        runningMessages[0].ShouldContain("ando-pre.csando");
        runningMessages[1].ShouldContain("ando-pre-bump.csando");
    }

    [Fact]
    public async Task RunHooksAsync_PostHooksRunInCorrectOrder()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        CreateHookScript(Path.Combine(scriptsDir, "ando-post.csando"), "Log.Info(\"general post\");");
        CreateHookScript(Path.Combine(scriptsDir, "ando-post-bump.csando"), "Log.Info(\"specific post\");");

        var context = new HookContext { Command = "bump" };
        await _runner.RunHooksAsync(HookRunner.HookType.Post, "bump", context);

        var runningMessages = _logger.InfoMessages.Where(m => m.Contains("Running hook:")).ToList();
        runningMessages.Count.ShouldBe(2);
        runningMessages[0].ShouldContain("ando-post.csando");
        runningMessages[1].ShouldContain("ando-post-bump.csando");
    }

    #endregion

    #region Pre-hook Abort Tests

    [Fact]
    public async Task RunHooksAsync_PreHookFailure_ReturnsFalse()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        // Script with syntax error - will fail compilation.
        CreateHookScript(Path.Combine(scriptsDir, "ando-pre-bump.csando"), "invalid code here!!!");

        var context = new HookContext { Command = "bump" };
        var result = await _runner.RunHooksAsync(HookRunner.HookType.Pre, "bump", context);

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task RunHooksAsync_PostHookFailure_ReturnsTrue()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        // Script with syntax error.
        CreateHookScript(Path.Combine(scriptsDir, "ando-post-bump.csando"), "invalid code here!!!");

        var context = new HookContext { Command = "bump" };
        var result = await _runner.RunHooksAsync(HookRunner.HookType.Post, "bump", context);

        // Post-hooks don't abort.
        result.ShouldBeTrue();
    }

    [Fact]
    public async Task RunHooksAsync_PreHookThrows_ReturnsFalse()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        // Script that throws.
        CreateHookScript(Path.Combine(scriptsDir, "ando-pre-bump.csando"),
            "throw new System.Exception(\"Hook aborted\");");

        var context = new HookContext { Command = "bump" };
        var result = await _runner.RunHooksAsync(HookRunner.HookType.Pre, "bump", context);

        result.ShouldBeFalse();
    }

    #endregion

    #region Environment Variable Tests

    [Fact]
    public async Task RunHooksAsync_SetsEnvironmentVariables()
    {
        var scriptsDir = Path.Combine(_testDir, "scripts");
        Directory.CreateDirectory(scriptsDir);

        // Script that reads environment variable.
        CreateHookScript(Path.Combine(scriptsDir, "ando-pre-bump.csando"),
            "var cmd = Env(\"ANDO_COMMAND\"); Log.Info($\"Command: {cmd}\");");

        var context = new HookContext { Command = "bump", BumpType = "patch" };
        await _runner.RunHooksAsync(HookRunner.HookType.Pre, "bump", context);

        _logger.InfoMessages.ShouldContain(m => m.Contains("Command: bump"));
    }

    #endregion

    private void CreateHookScript(string path, string content)
    {
        File.WriteAllText(path, content);
    }
}
