// =============================================================================
// PlaywrightOperationsTests.cs
//
// Summary: Unit tests for PlaywrightOperations class.
//
// Tests verify that:
// - Test() registers the correct step with default options
// - Test() with various options adds the correct flags
// - Install() registers the correct step
// - Test() with UseNpmScript uses npm instead of npx
// =============================================================================

using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class PlaywrightOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private PlaywrightOperations CreatePlaywright() =>
        new PlaywrightOperations(_registry, _logger, () => _executor);

    private static DirectoryRef TestDir(string path = "./test-e2e") =>
        new DirectoryRef(path);

    [Fact]
    public void Test_RegistersStep()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir);

        Assert.Single(_registry.Steps);
        Assert.Equal("Playwright.Test", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Test_ExecutesNpxPlaywrightTest()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir);
        await _registry.Steps[0].Execute();

        Assert.Single(_executor.ExecutedCommands);
        _executor.WasExecuted("npx", "playwright", "test").ShouldBeTrue();
    }

    [Fact]
    public async Task Test_WithHeaded_AddsHeadedFlag()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.Headed = true);
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("npx");
        cmd.HasArg("playwright").ShouldBeTrue();
        cmd.HasArg("test").ShouldBeTrue();
        cmd.HasArg("--headed").ShouldBeTrue();
    }

    [Fact]
    public async Task Test_WithProject_AddsProjectFlag()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.Project = "chromium");
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--project").ShouldBeTrue();
        cmd.HasArg("chromium").ShouldBeTrue();
    }

    [Fact]
    public async Task Test_WithUI_AddsUIFlag()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.UI = true);
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--ui").ShouldBeTrue();
    }

    [Fact]
    public async Task Test_WithWorkers_AddsWorkersFlag()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.Workers = 4);
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--workers").ShouldBeTrue();
        cmd.HasArg("4").ShouldBeTrue();
    }

    [Fact]
    public async Task Test_WithReporter_AddsReporterFlag()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.Reporter = "html");
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--reporter").ShouldBeTrue();
        cmd.HasArg("html").ShouldBeTrue();
    }

    [Fact]
    public async Task Test_WithGrep_AddsGrepFlag()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.Grep = "login");
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--grep").ShouldBeTrue();
        cmd.HasArg("login").ShouldBeTrue();
    }

    [Fact]
    public async Task Test_WithUpdateSnapshots_AddsUpdateSnapshotsFlag()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.UpdateSnapshots = true);
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--update-snapshots").ShouldBeTrue();
    }

    [Fact]
    public void Test_WithUseNpmScript_RegistersCorrectStepName()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.UseNpmScript = true);

        Assert.Single(_registry.Steps);
        Assert.Equal("Playwright.Test(test)", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Test_WithUseNpmScript_ExecutesNpmRunTest()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o => o.UseNpmScript = true);
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("npm");
        cmd.HasArg("run").ShouldBeTrue();
        cmd.HasArg("test").ShouldBeTrue();
    }

    [Fact]
    public async Task Test_WithCustomNpmScriptName_UsesCustomName()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o =>
        {
            o.UseNpmScript = true;
            o.NpmScriptName = "e2e";
        });
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("npm");
        cmd.HasArg("run").ShouldBeTrue();
        cmd.HasArg("e2e").ShouldBeTrue();
        Assert.Equal("Playwright.Test(e2e)", _registry.Steps[0].Name);
    }

    [Fact]
    public void Install_RegistersStep()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Install(dir);

        Assert.Single(_registry.Steps);
        Assert.Equal("Playwright.Install", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Install_ExecutesNpxPlaywrightInstall()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Install(dir);
        await _registry.Steps[0].Execute();

        _executor.WasExecuted("npx", "playwright", "install").ShouldBeTrue();
    }

    [Fact]
    public void DirectoryRef_SetsContextToDirectoryName()
    {
        var playwright = CreatePlaywright();
        var dir = new DirectoryRef("/path/to/e2e-tests");

        playwright.Test(dir);

        Assert.Equal("e2e-tests", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task Test_WithMultipleOptions_AddsAllFlags()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();

        playwright.Test(dir, o =>
        {
            o.Project = "chromium";
            o.Headed = true;
            o.Workers = 2;
            o.Reporter = "list";
        });
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--project").ShouldBeTrue();
        cmd.HasArg("chromium").ShouldBeTrue();
        cmd.HasArg("--headed").ShouldBeTrue();
        cmd.HasArg("--workers").ShouldBeTrue();
        cmd.HasArg("2").ShouldBeTrue();
        cmd.HasArg("--reporter").ShouldBeTrue();
        cmd.HasArg("list").ShouldBeTrue();
    }

    [Fact]
    public async Task CommandFailure_ReturnsFalse()
    {
        var playwright = CreatePlaywright();
        var dir = TestDir();
        _executor.SimulateFailure = true;

        playwright.Test(dir);
        var result = await _registry.Steps[0].Execute();

        result.ShouldBeFalse();
    }
}
