// =============================================================================
// ShellOperationsTests.cs
//
// Unit tests for ShellOperations command execution.
// =============================================================================

using Ando.Hooks;
using Ando.Tests.TestFixtures;
using Shouldly;

namespace Ando.Tests.Unit.Hooks;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Unit")]
public class ShellOperationsTests
{
    private readonly ShellOperations _shell;
    private readonly TestLogger _logger;

    public ShellOperationsTests()
    {
        _logger = new TestLogger();
        _shell = new ShellOperations(Directory.GetCurrentDirectory(), _logger);
    }

    [Fact]
    public async Task RunAsync_SuccessfulCommand_ReturnsZeroExitCode()
    {
        var result = await _shell.RunAsync("echo", "hello");

        result.ExitCode.ShouldBe(0);
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task RunAsync_SuccessfulCommand_CapturesOutput()
    {
        var result = await _shell.RunAsync("echo", "hello world");

        result.Output.ShouldContain("hello world");
    }

    [Fact]
    public async Task RunAsync_FailedCommand_ReturnsNonZeroExitCode()
    {
        var result = await _shell.RunAsync("ls", "/nonexistent-path-that-does-not-exist");

        result.ExitCode.ShouldNotBe(0);
        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task RunAsync_FailedCommand_CapturesError()
    {
        var result = await _shell.RunAsync("ls", "/nonexistent-path-that-does-not-exist");

        result.Error.ShouldNotBeEmpty();
    }

    [Fact]
    public void Run_SynchronousExecution_Works()
    {
        var result = _shell.Run("echo", "sync test");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldContain("sync test");
    }

    [Fact]
    public async Task RunAsync_MultipleArguments()
    {
        var result = await _shell.RunAsync("echo", "arg1", "arg2", "arg3");

        result.Output.ShouldContain("arg1");
        result.Output.ShouldContain("arg2");
        result.Output.ShouldContain("arg3");
    }

    [Fact]
    public async Task RunAsync_NoArguments()
    {
        var result = await _shell.RunAsync("pwd");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldNotBeEmpty();
    }

    [Fact]
    public async Task RunAsync_WithLogger_StreamsOutputToLog()
    {
        var result = await _shell.RunAsync("echo", "logged output");

        result.Success.ShouldBeTrue();
        _logger.InfoMessages.ShouldContain(m => m.Contains("logged output"));
    }

    [Fact]
    public async Task RunAsync_WithLogger_StreamsStderrAsWarning()
    {
        // Use ls on a non-existent path to trigger stderr output.
        var result = await _shell.RunAsync("ls", "/nonexistent-test-path-xyz");

        // ls writes the error to stderr.
        _logger.WarningMessages.ShouldContain(m => m.Contains("nonexistent-test-path-xyz"));
    }

    [Fact]
    public async Task RunAsync_ShowOutputFalse_DoesNotLog()
    {
        var result = await _shell.RunAsync("echo", showOutput: false, "silent output");

        result.Success.ShouldBeTrue();
        result.Output.ShouldContain("silent output");
        _logger.InfoMessages.ShouldNotContain(m => m.Contains("silent output"));
    }

    [Fact]
    public void Run_ShowOutputFalse_DoesNotLog()
    {
        var result = _shell.Run("echo", showOutput: false, "silent sync");

        result.Success.ShouldBeTrue();
        result.Output.ShouldContain("silent sync");
        _logger.InfoMessages.ShouldNotContain(m => m.Contains("silent sync"));
    }

    [Fact]
    public async Task RunAsync_WithoutLogger_StillCapturesOutput()
    {
        var shellWithoutLogger = new ShellOperations(Directory.GetCurrentDirectory());

        var result = await shellWithoutLogger.RunAsync("echo", "no logger");

        result.Success.ShouldBeTrue();
        result.Output.ShouldContain("no logger");
    }
}
