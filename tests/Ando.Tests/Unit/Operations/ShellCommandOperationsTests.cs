// =============================================================================
// ShellCommandOperationsTests.cs
//
// Summary: Tests for Shell.Run, Shell.Bash and Azure.Cli (CommandStep).
// =============================================================================

using Ando.Execution;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class ShellCommandOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private ShellCommandOperations CreateShell() => new(_registry, _logger, () => _executor);

    private Task<WorkflowResult> RunAsync() =>
        new WorkflowRunner(_registry, _logger).RunAsync(new BuildOptions());

    [Fact]
    public async Task Run_PassesCommandAndArgsThrough()
    {
        CreateShell().Run("zip", "-qr", "../web.zip", ".");

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        _registry.Steps[0].Name.ShouldBe("Shell.Run");
        _registry.Steps[0].Context.ShouldBe("zip");
        var command = _executor.ExecutedCommands.ShouldHaveSingleItem();
        command.Command.ShouldBe("zip");
        command.Args.ShouldBe(["-qr", "../web.zip", "."]);
    }

    [Fact]
    public async Task Bash_UsesStrictModeAndScriptAsCArgument()
    {
        CreateShell().Bash("source lib.sh && do_thing\necho done");

        await RunAsync();

        _registry.Steps[0].Name.ShouldBe("Shell.Bash");
        _registry.Steps[0].Context.ShouldBe("source lib.sh && do_thing");
        var command = _executor.ExecutedCommands.ShouldHaveSingleItem();
        command.Command.ShouldBe("bash");
        command.Args.ShouldBe(["-euo", "pipefail", "-c", "source lib.sh && do_thing\necho done"]);
    }

    [Fact]
    public void Bash_LongFirstLine_IsTruncatedInContext()
    {
        CreateShell().Bash(new string('x', 100));

        _registry.Steps[0].Context!.Length.ShouldBe(60);
        _registry.Steps[0].Context!.ShouldEndWith("...");
    }

    [Theory]
    [InlineData("artifacts/web", "/workspace/artifacts/web")]
    [InlineData("./artifacts/web", "/workspace/artifacts/web")]
    [InlineData("/abs/dir", "/abs/dir")]
    public async Task InDirectory_RelativeBecomesWorkspacePath(string given, string expected)
    {
        CreateShell().Run("ls").InDirectory(given);

        await RunAsync();

        _executor.LastCommand!.Options!.WorkingDirectory.ShouldBe(expected);
    }

    [Fact]
    public async Task NoInDirectory_LeavesWorkingDirectoryUnset()
    {
        CreateShell().Run("ls");

        await RunAsync();

        _executor.LastCommand!.Options!.WorkingDirectory.ShouldBeNull();
    }

    [Fact]
    public async Task Fluent_TimeoutEnvAndQuiet_ReachCommandOptions()
    {
        CreateShell().Run("tool")
            .WithTimeout(TimeSpan.FromSeconds(90))
            .WithEnv("A", "1")
            .WithEnv("B", "2")
            .Quiet();

        await RunAsync();

        var options = _executor.LastCommand!.Options!;
        options.TimeoutMs.ShouldBe(90_000);
        options.SuppressOutput.ShouldBeTrue();
        options.Environment["A"].ShouldBe("1");
        options.Environment["B"].ShouldBe("2");
    }

    [Fact]
    public async Task Defaults_AreNotQuietAndUseDefaultTimeout()
    {
        CreateShell().Run("tool");

        await RunAsync();

        var options = _executor.LastCommand!.Options!;
        options.SuppressOutput.ShouldBeFalse();
        options.TimeoutMs.ShouldBe(CommandOptions.DefaultTimeoutMs);
    }

    [Fact]
    public void WithTimeout_NonPositive_Throws()
    {
        var step = CreateShell().Run("tool");

        Should.Throw<ArgumentOutOfRangeException>(() => step.WithTimeout(TimeSpan.Zero));
    }

    [Fact]
    public async Task Value_IsCapturedTrimmedFromOutput()
    {
        _executor.SimulatedOutput = "  some-value\n";
        var step = CreateShell().Run("tool");

        step.HasValue.ShouldBeFalse();
        await RunAsync();

        step.Value.ShouldBe("some-value");
        step.ExitCode.ShouldBe(0);
    }

    [Fact]
    public async Task NonZeroExit_FailsStepAndBuild()
    {
        _executor.SimulateFailure = true;
        _executor.FailureExitCode = 7;
        var step = CreateShell().Run("tool");

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        step.ExitCode.ShouldBe(7);
        _logger.ErrorMessages.ShouldContain(m => m.Contains("exited with 7"));
    }

    [Fact]
    public async Task AllowFailure_PassesAndKeepsExitCode()
    {
        _executor.FailingCommands.Add("tool");
        _executor.FailureExitCode = 3;
        var shell = CreateShell();
        var step = shell.Run("tool").AllowFailure();
        shell.Run("next");

        var result = await RunAsync();

        result.Success.ShouldBeTrue();
        step.ExitCode.ShouldBe(3);
        _executor.ExecutedCommands.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Timeout_ThrowsAndFailsStepEvenWithAllowFailure()
    {
        _executor.SimulateTimeout = true;
        CreateShell().Run("tool").AllowFailure();

        var result = await RunAsync();

        result.Success.ShouldBeFalse();
        result.StepResults.Single().Exception.ShouldBeOfType<TimeoutException>();
    }

    [Fact]
    public async Task DefaultCommandTimeout_IsUsedForNewSteps()
    {
        _registry.DefaultCommandTimeoutMs = 42_000;
        CreateShell().Run("tool");

        await RunAsync();

        _executor.LastCommand!.Options!.TimeoutMs.ShouldBe(42_000);
    }

    [Fact]
    public async Task AzureCli_RunsAzWithArgsAndCapturesJson()
    {
        _executor.SimulatedOutput = """{"siteConfig":{"ipSecurityRestrictionsDefaultAction":"Deny"}}""";
        var azure = new AzureOperations(_registry, _logger, () => _executor);

        var show = azure.Cli("webapp", "show", "-g", "rg", "-n", "app", "--slot", "release");
        await RunAsync();

        _registry.Steps[0].Name.ShouldBe("Azure.Cli");
        _registry.Steps[0].Context.ShouldBe("webapp show");
        var command = _executor.ExecutedCommands.ShouldHaveSingleItem();
        command.Command.ShouldBe("az");
        command.Args.ShouldBe(["webapp", "show", "-g", "rg", "-n", "app", "--slot", "release"]);
        show.Json("siteConfig.ipSecurityRestrictionsDefaultAction").ShouldBe("Deny");
    }
}
