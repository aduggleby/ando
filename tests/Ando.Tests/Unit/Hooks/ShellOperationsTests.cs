// =============================================================================
// ShellOperationsTests.cs
//
// Unit tests for ShellOperations command execution.
// =============================================================================

using Ando.Hooks;
using Shouldly;

namespace Ando.Tests.Unit.Hooks;

[Collection("DirectoryChangingTests")]
[Trait("Category", "Unit")]
public class ShellOperationsTests
{
    private readonly ShellOperations _shell;

    public ShellOperationsTests()
    {
        _shell = new ShellOperations(Directory.GetCurrentDirectory());
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
}
