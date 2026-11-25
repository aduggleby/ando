// =============================================================================
// HookCheckTests.cs
//
// Summary: Tests for `ando hook check`, hook discovery (scripts/ shadows the
// root), the shadowed-hook warning and the `run -f <hook>` refusal.
//
// Design Decisions:
// - Each test uses its own temp project folder with real hook files.
// - The command is driven through HookCommand with a TestLogger, so nothing
//   touches the console or the current directory.
// =============================================================================

using Ando.Cli;
using Ando.Cli.Commands;
using Ando.Hooks;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Hooks;

[Trait("Category", "Unit")]
public class HookCheckTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ando-hookcheck-").FullName;
    private readonly TestLogger _logger = new();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private const string ValidHook = """
        Log.Info($"root is {Root}");
        var v = Env("ANDO_NEW_VERSION", required: false);
        await Shell.RunAsync("echo", "hi");
        """;

    // Uses the build-script API (Dotnet), which hooks do not have.
    private const string BuildScriptApi = """
        var project = Dotnet.Project("./src/App/App.csproj");
        Dotnet.Build(project);
        """;

    [Theory]
    [InlineData("ando-pre.csando", true)]
    [InlineData("ando-post.csando", true)]
    [InlineData("ando-pre-bump.csando", true)]
    [InlineData("scripts/ando-post-release.csando", true)]
    [InlineData("/abs/path/ANDO-PRE-RUN.csando", true)]
    [InlineData("build.csando", false)]
    [InlineData("ando-pre.cs", false)]
    [InlineData("ando-prepare.csando", false)]
    [InlineData("deploy.csando", false)]
    public void IsHookFileName_MatchesOnlyHookNames(string path, bool expected) =>
        HookRunner.IsHookFileName(path).ShouldBe(expected);

    [Fact]
    public async Task Check_ValidHook_PrintsOkAndReturnsZero()
    {
        var hook = Write("ando-pre.csando", ValidHook);

        var exit = await new HookCommand(_logger).ExecuteAsync(["check", hook], _root);

        exit.ShouldBe(0);
        _logger.InfoMessages.ShouldContain(m => m.StartsWith("OK") && m.Contains("ando-pre.csando"));
    }

    [Fact]
    public async Task Check_HookUsingBuildApi_PrintsDiagnosticsAndReturnsOne()
    {
        var hook = Write("scripts/ando-pre-bump.csando", BuildScriptApi);

        var exit = await new HookCommand(_logger).ExecuteAsync(["check", hook], _root);

        exit.ShouldBe(1);
        _logger.ErrorMessages.ShouldContain(m => m.StartsWith("FAIL"));
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Dotnet"));
    }

    [Fact]
    public async Task Check_DoesNotRunTheHook()
    {
        var marker = Path.Combine(_root, "ran.txt");
        var hook = Write("ando-post.csando", $"File.WriteAllText(@\"{marker}\", \"x\");");

        (await new HookCommand(_logger).ExecuteAsync(["check", hook], _root)).ShouldBe(0);

        File.Exists(marker).ShouldBeFalse();
    }

    [Fact]
    public async Task Check_NoColorOption_IsAccepted()
    {
        var hook = Write("ando-pre.csando", ValidHook);

        (await new HookCommand(_logger).ExecuteAsync(["check", "--no-color", hook], _root)).ShouldBe(0);
    }

    [Fact]
    public async Task Check_MissingFile_ReturnsTwo()
    {
        var exit = await new HookCommand(_logger).ExecuteAsync(["check", Path.Combine(_root, "ando-pre.csando")], _root);

        exit.ShouldBe(2);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("Hook file not found"));
    }

    [Theory]
    [InlineData]
    [InlineData("verify")]
    [InlineData("check", "--all")]
    public async Task WrongArguments_ReturnTwoWithUsage(params string[] args)
    {
        var exit = await new HookCommand(_logger).ExecuteAsync(args, _root);

        exit.ShouldBe(2);
        _logger.ErrorMessages.ShouldContain(HookCommand.Usage);
    }

    [Fact]
    public async Task CheckAll_ChecksBothLocationsAndSkipsShadowedRootFile()
    {
        Write("scripts/ando-pre.csando", ValidHook);
        Write("ando-pre.csando", BuildScriptApi); // shadowed: never runs, so not compiled
        Write("ando-post-bump.csando", ValidHook);
        Write("build.csando", BuildScriptApi); // not a hook

        var exit = await new HookCommand(_logger).ExecuteAsync(["check"], _root);

        exit.ShouldBe(0);
        _logger.InfoMessages.Count(m => m.StartsWith("OK")).ShouldBe(2);
        _logger.InfoMessages.ShouldContain(m => m.Contains(Path.Combine("scripts", "ando-pre.csando")));
        _logger.InfoMessages.ShouldContain(m => m.Contains("ando-post-bump.csando"));
        _logger.WarningMessages.ShouldContain(m => m.Contains("in the project root is ignored") && m.Contains("not checked"));
    }

    [Fact]
    public async Task CheckAll_BrokenActiveHook_ReturnsOne()
    {
        Write("ando-post.csando", BuildScriptApi);

        (await new HookCommand(_logger).ExecuteAsync(["check"], _root)).ShouldBe(1);
    }

    [Fact]
    public async Task CheckAll_NoHooks_ReturnsZero()
    {
        (await new HookCommand(_logger).ExecuteAsync(["check"], _root)).ShouldBe(0);
        _logger.InfoMessages.ShouldContain(m => m.Contains("No hook scripts found"));
    }

    [Fact]
    public void Discover_PrefersScriptsAndReportsShadowedRoot()
    {
        var active = Write("scripts/ando-pre.csando", ValidHook);
        var shadowed = Write("ando-pre.csando", ValidHook);
        var rootOnly = Write("ando-post.csando", ValidHook);

        var hooks = HookRunner.Discover(_root);

        hooks.ShouldBe([
            new HookRunner.HookLocation(rootOnly, null),
            new HookRunner.HookLocation(active, shadowed)
        ]);
    }

    [Fact]
    public async Task RunHooks_ShadowedRootHook_LogsWarningAndRunsScriptsVersion()
    {
        Write("scripts/ando-pre-bump.csando", "Log.Info(\"scripts\");");
        Write("ando-pre-bump.csando", "Log.Info(\"root\");");

        await new HookRunner(_root, _logger).RunHooksAsync(HookRunner.HookType.Pre, "bump", new HookContext { Command = "bump" });

        _logger.WarningMessages.ShouldContain(m =>
            m.Contains("Hook ando-pre-bump.csando in the project root is ignored") && m.Contains("scripts"));
        _logger.InfoMessages.ShouldContain(m => m.Contains("scripts"));
        _logger.InfoMessages.ShouldNotContain(m => m.Contains("root"));
    }

    [Fact]
    public async Task RunHooks_NoShadow_NoWarning()
    {
        Write("scripts/ando-pre-bump.csando", "Log.Info(\"scripts\");");

        await new HookRunner(_root, _logger).RunHooksAsync(HookRunner.HookType.Pre, "bump", new HookContext { Command = "bump" });

        _logger.WarningMessages.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("run")]
    [InlineData("verify")]
    public void HookScriptError_HookFile_NamesHookAndCheckCommand(string command)
    {
        var error = AndoCli.HookScriptError("scripts/ando-pre.csando", command);

        error.ShouldNotBeNull();
        error.ShouldContain("ando-pre.csando is a hook script, not a build script");
        error.ShouldContain($"`ando {command}`");
        error.ShouldContain("ando hook check scripts/ando-pre.csando");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("build.csando")]
    [InlineData("deploy.csando")]
    public void HookScriptError_BuildScript_ReturnsNull(string? file) =>
        AndoCli.HookScriptError(file, "run").ShouldBeNull();
}
