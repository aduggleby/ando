// =============================================================================
// PortabilityCliTests.cs
//
// Summary: Unit tests for the export/import command-line argument parsing and
// for the CLI-mode detection Program.cs uses.
//
// Design Decisions:
// - Only parsing and the usage/exit-code path are tested here; running a real
//   export/import needs a database and is covered by the integration tests and
//   the manual CLI run
// =============================================================================

using Ando.Server.DataPortability;

namespace Ando.Server.Tests.Unit.DataPortability;

/// <summary>
/// Tests for <see cref="PortabilityCli"/> argument handling.
/// </summary>
public class PortabilityCliTests
{
    [Theory]
    [InlineData(new[] { "export" }, true)]
    [InlineData(new[] { "import", "--input", "x" }, true)]
    [InlineData(new string[0], false)]
    [InlineData(new[] { "--urls", "http://0.0.0.0:8080" }, false)]
    [InlineData(new[] { "Export" }, false)]
    public void IsPortabilityCommand_OnlyMatchesExactFirstArgument(string[] args, bool expected)
    {
        PortabilityCli.IsPortabilityCommand(args).ShouldBe(expected);
    }

    [Fact]
    public void Parse_Export_WithOutputAndOverwrite()
    {
        var command = PortabilityCli.Parse(["export", "--output", "/data/x.jsonl", "--overwrite"]);

        command.ShouldBe(new PortabilityCommand(PortabilityCommandKind.Export, "/data/x.jsonl", Overwrite: true, Force: false, ShowHelp: false));
    }

    [Fact]
    public void Parse_Import_WithInputAndForce_FlagBeforePath()
    {
        var command = PortabilityCli.Parse(["import", "--force", "--input", "in.jsonl"]);

        command.ShouldBe(new PortabilityCommand(PortabilityCommandKind.Import, "in.jsonl", Overwrite: false, Force: true, ShowHelp: false));
    }

    [Fact]
    public void Parse_Import_WithoutForce_DefaultsToFalse()
    {
        PortabilityCli.Parse(["import", "--input", "in.jsonl"]).Force.ShouldBeFalse();
    }

    [Theory]
    [InlineData("export", "--help")]
    [InlineData("import", "-h")]
    public void Parse_Help_DoesNotRequirePath(string command, string flag)
    {
        PortabilityCli.Parse([command, flag]).ShowHelp.ShouldBeTrue();
    }

    [Theory]
    [InlineData(new[] { "export" }, "requires --output")]
    [InlineData(new[] { "import" }, "requires --input")]
    [InlineData(new[] { "export", "--output" }, "--output requires a file path")]
    [InlineData(new[] { "export", "--output", "--overwrite" }, "--output requires a file path")]
    [InlineData(new[] { "export", "--input", "x" }, "Unknown argument '--input'")]
    [InlineData(new[] { "import", "--output", "x" }, "Unknown argument '--output'")]
    [InlineData(new[] { "export", "--output", "x", "--force" }, "Unknown argument '--force'")]
    [InlineData(new[] { "import", "--input", "x", "--overwrite" }, "Unknown argument '--overwrite'")]
    [InlineData(new[] { "import", "--input", "a", "--input", "b" }, "more than once")]
    [InlineData(new[] { "import", "in.jsonl" }, "Unknown argument 'in.jsonl'")]
    public void Parse_InvalidArguments_Throw(string[] args, string expectedMessage)
    {
        var ex = Should.Throw<PortabilityUsageException>(() => PortabilityCli.Parse(args));
        ex.Message.ShouldContain(expectedMessage);
    }

    [Fact]
    public async Task RunAsync_InvalidArguments_PrintsUsageAndReturnsOne()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await PortabilityCli.RunAsync(["import"], stdout, stderr);

        exitCode.ShouldBe(1);
        stderr.ToString().ShouldContain("Error: import requires --input <path>.");
        stderr.ToString().ShouldContain("Usage:");
        stdout.ToString().ShouldBeEmpty();
    }

    [Fact]
    public async Task RunAsync_Help_PrintsUsageAndReturnsZero()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();

        var exitCode = await PortabilityCli.RunAsync(["export", "--help"], stdout, stderr);

        exitCode.ShouldBe(0);
        stdout.ToString().ShouldContain("dotnet Ando.Server.dll export --output <path> [--overwrite]");
        stdout.ToString().ShouldContain("dotnet Ando.Server.dll import --input <path> [--force]");
        stderr.ToString().ShouldBeEmpty();
    }
}
