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
        output.ShouldContain("--container");
        output.ShouldContain("--all");
    }

    [Fact]
    public async Task Help_ShowsAllCommands()
    {
        var cli = new AndoCli(new[] { "help" });
        var (_, output) = await RunCliAndCaptureOutput(cli);

        // Verify all commands are listed in help.
        output.ShouldContain("run");
        output.ShouldContain("verify");
        output.ShouldContain("commit");
        output.ShouldContain("bump");
        output.ShouldContain("docs");
        output.ShouldContain("release");
        output.ShouldContain("clean");
    }

    [Fact]
    public async Task Help_ShowsReleaseOptions()
    {
        var cli = new AndoCli(new[] { "help" });
        var (_, output) = await RunCliAndCaptureOutput(cli);

        output.ShouldContain("--all");
        output.ShouldContain("--dry-run");
        output.ShouldContain("patch");
        output.ShouldContain("minor");
        output.ShouldContain("major");
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

    #region Unknown Command Tests

    [Theory]
    [InlineData("foo")]
    [InlineData("unknown")]
    [InlineData("build")]
    [InlineData("test")]
    public async Task UnknownCommand_ReturnsExitCode1(string unknownCommand)
    {
        var cli = new AndoCli(new[] { unknownCommand });
        var (exitCode, _) = await RunCliAndCaptureOutput(cli);

        exitCode.ShouldBe(1);
    }

    [Fact]
    public async Task UnknownCommand_ShowsErrorMessage()
    {
        var cli = new AndoCli(new[] { "notacommand" });
        var (_, output) = await RunCliAndCaptureOutput(cli);

        output.ShouldContain("Unknown command: notacommand");
    }

    [Fact]
    public async Task UnknownCommand_ShowsHelpInfo()
    {
        var cli = new AndoCli(new[] { "notacommand" });
        var (_, output) = await RunCliAndCaptureOutput(cli);

        output.ShouldContain("Usage:");
        output.ShouldContain("Commands:");
    }

    #endregion

    #region Version Command Tests

    [Theory]
    [InlineData("-v")]
    [InlineData("--version")]
    public async Task VersionCommand_ReturnsZeroExitCode(string versionFlag)
    {
        var cli = new AndoCli(new[] { versionFlag });
        var (exitCode, _) = await RunCliAndCaptureOutput(cli);

        exitCode.ShouldBe(0);
    }

    [Theory]
    [InlineData("-v")]
    [InlineData("--version")]
    public async Task VersionCommand_ShowsVersionNumber(string versionFlag)
    {
        var cli = new AndoCli(new[] { versionFlag });
        var (_, output) = await RunCliAndCaptureOutput(cli);

        output.ShouldContain("ando ");
        // Version format should be like "ando 0.9.48" or similar.
        output.ShouldMatch(@"ando \d+\.\d+\.\d+");
    }

    #endregion

    #region Known Commands Tests

    [Theory]
    [InlineData("run")]
    [InlineData("verify")]
    [InlineData("commit")]
    [InlineData("bump")]
    [InlineData("docs")]
    [InlineData("release")]
    [InlineData("clean")]
    [InlineData("help")]
    public async Task KnownCommands_DoNotReturnUnknownCommandError(string command)
    {
        var cli = new AndoCli(new[] { command });
        var (_, output) = await RunCliAndCaptureOutput(cli);

        output.ShouldNotContain("Unknown command:");
    }

    #endregion

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
