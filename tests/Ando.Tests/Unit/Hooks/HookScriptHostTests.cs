// =============================================================================
// HookScriptHostTests.cs
//
// Unit tests for HookScriptHost Roslyn script execution.
// =============================================================================

using Ando.Hooks;
using Ando.Tests.TestFixtures;
using Shouldly;

namespace Ando.Tests.Unit.Hooks;

[Trait("Category", "Unit")]
public class HookScriptHostTests : IDisposable
{
    private readonly string _testDir;
    private readonly TestLogger _logger;
    private readonly HookScriptHost _host;

    public HookScriptHostTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"hook-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _logger = new TestLogger();
        _host = new HookScriptHost(_testDir, _logger);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    #region Basic Execution Tests

    [Fact]
    public async Task ExecuteAsync_SimpleScript_Executes()
    {
        var script = CreateScript("Log.Info(\"Hello from hook!\");");

        await _host.ExecuteAsync(script, new Dictionary<string, string>());

        _logger.InfoMessages.ShouldContain("  Hello from hook!");
    }

    [Fact]
    public async Task ExecuteAsync_AccessesEnvironmentVariables()
    {
        var script = CreateScript("var value = Env(\"TEST_VAR\"); Log.Info($\"Value: {value}\");");

        await _host.ExecuteAsync(script, new Dictionary<string, string> { ["TEST_VAR"] = "test-value" });

        _logger.InfoMessages.ShouldContain(m => m.Contains("  Value: test-value"));
    }

    [Fact]
    public async Task ExecuteAsync_RestoresEnvironmentVariables()
    {
        var originalValue = "original";
        Environment.SetEnvironmentVariable("RESTORE_TEST", originalValue);

        var script = CreateScript("Log.Info(Env(\"RESTORE_TEST\"));");

        await _host.ExecuteAsync(script, new Dictionary<string, string> { ["RESTORE_TEST"] = "modified" });

        Environment.GetEnvironmentVariable("RESTORE_TEST").ShouldBe(originalValue);

        // Cleanup.
        Environment.SetEnvironmentVariable("RESTORE_TEST", null);
    }

    [Fact]
    public async Task ExecuteAsync_CanAccessProjectRoot()
    {
        var script = CreateScript("Log.Info($\"Root: {Root}\");");

        await _host.ExecuteAsync(script, new Dictionary<string, string>());

        _logger.InfoMessages.ShouldContain(m => m.Contains($"  Root: {_testDir}"));
    }

    #endregion

    #region Error Handling Tests

    [Fact]
    public async Task ExecuteAsync_MissingFile_ThrowsFileNotFoundException()
    {
        var missingPath = Path.Combine(_testDir, "nonexistent.csando");

        await Should.ThrowAsync<FileNotFoundException>(async () =>
            await _host.ExecuteAsync(missingPath, new Dictionary<string, string>()));
    }

    [Fact]
    public async Task ExecuteAsync_CompilationError_ThrowsHookAbortException()
    {
        var script = CreateScript("this is not valid C# code!!!");

        var ex = await Should.ThrowAsync<HookAbortException>(async () =>
            await _host.ExecuteAsync(script, new Dictionary<string, string>()));

        ex.Message.ShouldContain("compilation failed");
    }

    [Fact]
    public async Task ExecuteAsync_RuntimeException_PropagatesException()
    {
        var script = CreateScript("throw new System.Exception(\"Runtime error!\");");

        await Should.ThrowAsync<Exception>(async () =>
            await _host.ExecuteAsync(script, new Dictionary<string, string>()));
    }

    #endregion

    #region Timeout Tests

    [Fact(Skip = "Roslyn scripts cannot cancel synchronous infinite loops")]
    public async Task ExecuteAsync_Timeout_ThrowsTimeoutException()
    {
        // Script that would run forever.
        // NOTE: Roslyn's CSharpScript.RunAsync doesn't support cancellation for
        // synchronous code, so this test would hang indefinitely.
        var script = CreateScript("while(true) { System.Threading.Thread.Sleep(100); }");

        await Should.ThrowAsync<TimeoutException>(async () =>
            await _host.ExecuteAsync(script, new Dictionary<string, string>(), timeoutMs: 500));
    }

    [Fact]
    public async Task ExecuteAsync_FastScript_CompletesWithinTimeout()
    {
        var script = CreateScript("Log.Info(\"Quick!\");");

        await Should.NotThrowAsync(async () =>
            await _host.ExecuteAsync(script, new Dictionary<string, string>(), timeoutMs: 5000));

        _logger.InfoMessages.ShouldContain("  Quick!");
    }

    #endregion

    #region Script Capabilities Tests

    [Fact]
    public async Task ExecuteAsync_CanUseSystemLinq()
    {
        var script = CreateScript(@"
            var numbers = new[] { 1, 2, 3, 4, 5 };
            var sum = numbers.Sum();
            Log.Info($""Sum: {sum}"");
        ");

        await _host.ExecuteAsync(script, new Dictionary<string, string>());

        _logger.InfoMessages.ShouldContain("  Sum: 15");
    }

    [Fact]
    public async Task ExecuteAsync_CanUseSystemIO()
    {
        var script = CreateScript(@"
            var exists = System.IO.Directory.Exists(Root);
            Log.Info($""Dir exists: {exists}"");
        ");

        await _host.ExecuteAsync(script, new Dictionary<string, string>());

        _logger.InfoMessages.ShouldContain("  Dir exists: True");
    }

    [Fact]
    public async Task ExecuteAsync_CanUseCollections()
    {
        var script = CreateScript(@"
            var dict = new Dictionary<string, int> { [""a""] = 1, [""b""] = 2 };
            Log.Info($""Count: {dict.Count}"");
        ");

        await _host.ExecuteAsync(script, new Dictionary<string, string>());

        _logger.InfoMessages.ShouldContain("  Count: 2");
    }

    [Fact]
    public async Task ExecuteAsync_CanUseDirectoryRef()
    {
        var subDir = Path.Combine(_testDir, "subdir");
        System.IO.Directory.CreateDirectory(subDir);

        var script = CreateScript(@"
            var dir = new DirectoryRef(""subdir"");
            Log.Info($""Ref: {dir.Path}"");
        ");

        await _host.ExecuteAsync(script, new Dictionary<string, string>());

        _logger.InfoMessages.ShouldContain("  Ref: subdir");
    }

    #endregion

    #region Helper Methods

    private string CreateScript(string content)
    {
        var path = Path.Combine(_testDir, $"hook-{Guid.NewGuid():N}.csando");
        File.WriteAllText(path, content);
        return path;
    }

    #endregion
}
