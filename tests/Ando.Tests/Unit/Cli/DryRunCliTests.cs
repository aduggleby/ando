// =============================================================================
// DryRunCliTests.cs
//
// Summary: End-to-end `ando run --dry-run` checks through AndoCli: missing
// required environment variables get placeholders and one warning, while the
// hidden --dry-run-output mode (Ando.StepTree) stays strict.
//
// Design Decisions:
// - Joins the ConsoleOutput collection because it redirects Console.Out.
// - Uses unique, never-set variable names and a fresh temp folder per test.
// =============================================================================

using Ando.Cli;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
[Collection("ConsoleOutput")]
public class DryRunCliTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ando-dryrun-cli-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string UniqueName() => "ANDO_TEST_MISSING_" + Guid.NewGuid().ToString("N").ToUpperInvariant();

    private string WriteScript(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public async Task DryRun_MissingRequiredEnv_PrintsTreeAndOneWarningAndExitsZero()
    {
        var a = UniqueName();
        var b = UniqueName();
        var script = WriteScript("build.csando", $$"""
            var first = Env("{{a}}");
            var second = Env("{{b}}");
            Env("{{a}}");
            Shell.Run("echo", "deploy");
            """);

        var (exitCode, output) = await RunCli("run", "--dry-run", "--no-env", "--no-color", "-f", script);

        exitCode.ShouldBe(0);
        output.ShouldContain("Shell.Run");
        output.ShouldContain(
            $"Dry run: these required environment variables are not set; placeholders were used: {a}, {b}. " +
            "A real run fails without them.");
        CountOccurrences(output, "placeholders were used").ShouldBe(1);
    }

    [Fact]
    public async Task DryRun_AllEnvSet_PrintsNoWarning()
    {
        var script = WriteScript("build.csando", """
            Shell.Run("echo", "hi");
            """);

        var (exitCode, output) = await RunCli("run", "--dry-run", "--no-env", "--no-color", "-f", script);

        exitCode.ShouldBe(0);
        output.ShouldNotContain("placeholders were used");
    }

    [Fact]
    public async Task DryRunOutput_StepTreeMode_MissingEnvStillFails()
    {
        var name = UniqueName();
        var script = WriteScript("build.csando", $"var x = Env(\"{name}\");");
        var tree = Path.Combine(_dir, "tree.txt");

        var (exitCode, output) = await RunCli(
            "run", "--dry-run", "--no-env", "--no-color", "-f", script, "--dry-run-output", tree);

        exitCode.ShouldBe(5);
        output.ShouldContain($"Required environment variable '{name}' is not set.");
        File.Exists(tree).ShouldBeFalse();
    }

    [Fact]
    public async Task DryRun_PlaceholderBreaksScript_ExitsFiveAndStillNamesVariable()
    {
        var name = UniqueName();
        var script = WriteScript("build.csando", $"var port = int.Parse(Env(\"{name}\")!);");

        var (exitCode, output) = await RunCli("run", "--dry-run", "--no-env", "--no-color", "-f", script);

        exitCode.ShouldBe(5);
        output.ShouldContain($"placeholders were used: {name}.");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        for (var i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + 1, StringComparison.Ordinal))
        {
            count++;
        }
        return count;
    }

    internal static async Task<(int exitCode, string output)> RunCli(params string[] args)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        using var outWriter = new StringWriter();
        using var errWriter = new StringWriter();
        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);
            var exitCode = await new AndoCli(args).RunAsync();
            return (exitCode, outWriter.ToString() + errWriter.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
        }
    }
}
