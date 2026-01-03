// =============================================================================
// CliArgumentTests.cs
//
// Summary: Unit tests for CLI argument parsing logic.
//
// Tests verify argument handling, flag parsing, and configuration options
// without executing full builds.
// =============================================================================

using Ando.Cli;

namespace Ando.Tests.Unit.Cli;
[Trait("Category", "Unit")]
[Collection("ConsoleOutput")]
public class CliArgumentTests
{
    [Fact]
    public async Task Help_ShowsUsageInfo()
    {
        var cli = new AndoCli(new[] { "help" });
        var (exitCode, output) = await RunCliAndCaptureOutput(cli);

        exitCode.ShouldBe(0);
        output.ShouldContain("Usage:");
        output.ShouldContain("Commands:");
        output.ShouldContain("run");
        output.ShouldContain("clean");
    }

    [Fact]
    public async Task HelpFlag_ShowsUsageInfo()
    {
        var cli = new AndoCli(new[] { "--help" });
        var (exitCode, output) = await RunCliAndCaptureOutput(cli);

        exitCode.ShouldBe(0);
        output.ShouldContain("Usage:");
    }

    [Fact]
    public async Task ShortHelpFlag_ShowsUsageInfo()
    {
        var cli = new AndoCli(new[] { "-h" });
        var (exitCode, output) = await RunCliAndCaptureOutput(cli);

        exitCode.ShouldBe(0);
        output.ShouldContain("Usage:");
    }

    [Fact]
    public async Task Help_ShowsRunOptions()
    {
        var cli = new AndoCli(new[] { "help" });
        var (_, output) = await RunCliAndCaptureOutput(cli);

        output.ShouldContain("--verbosity");
        output.ShouldContain("--no-color");
        output.ShouldContain("--cold");
        output.ShouldContain("--image");
        output.ShouldContain("--dind");
    }

    [Fact]
    public async Task Help_ShowsCleanOptions()
    {
        var cli = new AndoCli(new[] { "help" });
        var (_, output) = await RunCliAndCaptureOutput(cli);

        output.ShouldContain("--artifacts");
        output.ShouldContain("--temp");
        output.ShouldContain("--cache");
        output.ShouldContain("--container");
        output.ShouldContain("--all");
    }

    [Theory]
    [InlineData("help")]
    [InlineData("--help")]
    [InlineData("-h")]
    public async Task HelpCommands_ReturnZeroExitCode(string arg)
    {
        var cli = new AndoCli(new[] { arg });
        var exitCode = await cli.RunAsync();

        exitCode.ShouldBe(0);
    }

    private static async Task<(int exitCode, string output)> RunCliAndCaptureOutput(AndoCli cli)
    {
        var originalOut = Console.Out;
        var originalErr = Console.Error;
        var outWriter = new StringWriter();
        var errWriter = new StringWriter();

        try
        {
            Console.SetOut(outWriter);
            Console.SetError(errWriter);

            var exitCode = await cli.RunAsync();

            var output = outWriter.ToString() + errWriter.ToString();
            return (exitCode, output);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalErr);
            outWriter.Dispose();
            errWriter.Dispose();
        }
    }
}
