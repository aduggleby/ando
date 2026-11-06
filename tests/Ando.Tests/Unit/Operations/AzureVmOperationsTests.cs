// =============================================================================
// AzureVmOperationsTests.cs
//
// Summary: Tests for Azure.Vm.RunCommand: script wrapping, parsing of
// `az vm run-command invoke -o json` output and step success/failure.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class AzureVmOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private static string RunCommandJson(string message) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            value = new[] { new { code = "ProvisioningState/succeeded", level = "Info", displayStatus = "Provisioning succeeded", message } }
        });

    // The realistic message from the task description.
    private const string RealisticJson =
        "{\"value\":[{\"code\":\"ProvisioningState/succeeded\",\"message\":\"Enable succeeded: \\n[stdout]\\nhello\\n__ANDO_EXIT_CODE=3\\n\\n[stderr]\\nboom\\n\"}]}";

    private static string Message(string stdout, string stderr = "") =>
        $"Enable succeeded: \n[stdout]\n{stdout}\n\n[stderr]\n{stderr}\n";

    [Fact]
    public void WrapScript_RunsScriptInSubshellAndEchoesExitMarker()
    {
        var wrapped = AzureVmOperations.WrapScript("exit 3");

        wrapped.ShouldBe("(\nexit 3\n)\n__ando_rc=$?\necho\necho \"__ANDO_EXIT_CODE=$__ando_rc\"");
    }

    [Fact]
    public void ParseResult_StdoutWithoutTrailingNewline_StillFindsMarker()
    {
        // printf hello: the wrapper's extra echo puts the marker on its own line.
        const string json = "{\"value\":[{\"message\":\"Enable succeeded: \\n[stdout]\\nhello\\n__ANDO_EXIT_CODE=0\\n\\n[stderr]\\n\"}]}";
        var (stdout, _, exitCode) = AzureVmOperations.ParseResult(json);
        stdout.ShouldBe("hello");
        exitCode.ShouldBe(0);
    }

    [Fact]
    public void ParseResult_RealisticOutput_ExtractsStdoutStderrAndExitCode()
    {
        var (stdout, stderr, exitCode) = AzureVmOperations.ParseResult(RealisticJson);

        stdout.ShouldBe("hello");
        stderr.ShouldBe("boom");
        exitCode.ShouldBe(3);
    }

    [Fact]
    public void ParseResult_Success_ExitCodeZero()
    {
        var (stdout, stderr, exitCode) = AzureVmOperations.ParseResult(
            RunCommandJson(Message("line1\nline2\n__ANDO_EXIT_CODE=0")));

        stdout.ShouldBe("line1\nline2");
        stderr.ShouldBe("");
        exitCode.ShouldBe(0);
    }

    [Fact]
    public void ParseResult_MissingMarker_ExitCodeNull()
    {
        var (stdout, _, exitCode) = AzureVmOperations.ParseResult(RunCommandJson(Message("only output")));

        stdout.ShouldBe("only output");
        exitCode.ShouldBeNull();
    }

    [Fact]
    public void ParseResult_NonNumericMarker_ExitCodeNullAndMarkerRemoved()
    {
        var (stdout, _, exitCode) = AzureVmOperations.ParseResult(
            RunCommandJson(Message("out\n__ANDO_EXIT_CODE=abc")));

        exitCode.ShouldBeNull();
        stdout.ShouldBe("out");
    }

    [Fact]
    public void ParseResult_CrLfLines_StillFindsMarker()
    {
        var (stdout, _, exitCode) = AzureVmOperations.ParseResult(
            RunCommandJson("Enable succeeded: \r\n[stdout]\r\nhi\r\n__ANDO_EXIT_CODE=0\r\n\r\n[stderr]\r\n"));

        exitCode.ShouldBe(0);
        stdout.ShouldBe("hi");
    }

    [Fact]
    public void ParseResult_NonJson_TreatsTextAsMessage()
    {
        var (stdout, _, exitCode) = AzureVmOperations.ParseResult("plain\n__ANDO_EXIT_CODE=5");

        exitCode.ShouldBe(5);
        stdout.ShouldBe("plain");
    }

    [Fact]
    public async Task RunCommand_Success_SetsStdoutValueAndPassesAzArgs()
    {
        _executor.SimulatedOutput = RunCommandJson(Message("deployed\n__ANDO_EXIT_CODE=0"));
        var output = new AzureVmOperations(_registry, _logger, () => _executor)
            .RunCommand("rg-eu", "docs-vm", "update-worker pkg.tar.gz", TimeSpan.FromMinutes(2));

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(new BuildOptions());

        result.Success.ShouldBeTrue();
        output.Value.ShouldBe("deployed");
        output.ExitCode.ShouldBe(0);
        _registry.Steps[0].Name.ShouldBe("Azure.Vm.RunCommand");
        _registry.Steps[0].Context.ShouldBe("rg-eu/docs-vm");
        var command = _executor.ExecutedCommands.ShouldHaveSingleItem();
        command.Command.ShouldBe("az");
        command.Args.ShouldBe(
        [
            "vm", "run-command", "invoke",
            "--resource-group", "rg-eu",
            "--name", "docs-vm",
            "--command-id", "RunShellScript",
            "--scripts", AzureVmOperations.WrapScript("update-worker pkg.tar.gz"),
            "--output", "json"
        ]);
        command.Options!.TimeoutMs.ShouldBe(120_000);
        command.Options.SuppressOutput.ShouldBeTrue();
        _logger.InfoMessages.ShouldContain("deployed");
    }

    [Fact]
    public async Task RunCommand_DefaultTimeoutIsThirtyMinutes()
    {
        _executor.SimulatedOutput = RunCommandJson(Message("__ANDO_EXIT_CODE=0"));
        new AzureVmOperations(_registry, _logger, () => _executor).RunCommand("rg", "vm", "true");

        await new WorkflowRunner(_registry, _logger).RunAsync(new BuildOptions());

        _executor.LastCommand!.Options!.TimeoutMs.ShouldBe(30 * 60 * 1000);
    }

    [Fact]
    public async Task RunCommand_ScriptExitsNonZero_FailsAndLogsStderrAsWarning()
    {
        _executor.SimulatedOutput = RealisticJson;
        var output = new AzureVmOperations(_registry, _logger, () => _executor).RunCommand("rg", "vm", "false");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(new BuildOptions());

        result.Success.ShouldBeFalse();
        output.ExitCode.ShouldBe(3);
        output.Value.ShouldBe("hello");
        _logger.WarningMessages.ShouldContain("boom");
        _logger.ErrorMessages.ShouldContain(m => m.Contains("exited with 3"));
    }

    [Fact]
    public async Task RunCommand_MissingMarker_Fails()
    {
        _executor.SimulatedOutput = RunCommandJson(Message("output without marker"));
        var output = new AzureVmOperations(_registry, _logger, () => _executor).RunCommand("rg", "vm", "true");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(new BuildOptions());

        result.Success.ShouldBeFalse();
        output.ExitCode.ShouldBe(-1);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("exit code is missing"));
    }

    [Fact]
    public async Task RunCommand_AzFails_FailsStep()
    {
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "az exploded";
        new AzureVmOperations(_registry, _logger, () => _executor).RunCommand("rg", "vm", "true");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(new BuildOptions());

        result.Success.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("az exploded"));
    }

    [Fact]
    public void AzureVm_IsReachableFromAzureOperations()
    {
        new AzureOperations(_registry, _logger, () => _executor).Vm.ShouldNotBeNull();
    }
}
