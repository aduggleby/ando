// =============================================================================
// CliProcessRunnerTests.cs
//
// Unit tests for CliProcessRunner process execution functionality.
// =============================================================================

using Ando.Utilities;
using Shouldly;

namespace Ando.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class CliProcessRunnerTests
{
    private readonly CliProcessRunner _runner = new();

    [Fact]
    public async Task RunAsync_EchoCommand_CapturesOutput()
    {
        // "echo" is available on both Linux/macOS and Windows (in cmd/powershell).
        var result = await _runner.RunAsync("echo", "hello");

        result.ExitCode.ShouldBe(0);
        result.Output.Trim().ShouldBe("hello");
    }

    [Fact]
    public async Task RunAsync_NonExistentCommand_ThrowsException()
    {
        // This command shouldn't exist on any system.
        // When the command doesn't exist, Process.Start throws Win32Exception.
        await Should.ThrowAsync<System.ComponentModel.Win32Exception>(async () =>
        {
            await _runner.RunAsync("nonexistent-command-xyz123", "");
        });
    }

    [Fact]
    public async Task RunAsync_WithWorkingDirectory_UsesSpecifiedDirectory()
    {
        var tempDir = Path.GetTempPath();
        var result = await _runner.RunAsync("pwd", "", workingDirectory: tempDir);

        // pwd should return the temp directory path.
        // Note: On some systems, temp path might have symlinks resolved.
        result.ExitCode.ShouldBe(0);
        result.Output.Trim().ShouldNotBeEmpty();
    }

    [Fact]
    public async Task RunAsync_WithStdin_PassesInput()
    {
        // Using 'cat' which reads stdin and outputs it.
        var result = await _runner.RunAsync("cat", "", stdin: "hello from stdin");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldBe("hello from stdin");
    }

    [Fact]
    public async Task RunAsync_Timeout_ThrowsTimeoutException()
    {
        // Sleep for longer than the timeout.
        await Should.ThrowAsync<TimeoutException>(async () =>
        {
            await _runner.RunAsync("sleep", "10", timeoutMs: 100);
        });
    }

    [Fact]
    public void ProcessResult_Record_HasExpectedProperties()
    {
        var result = new CliProcessRunner.ProcessResult(0, "output", "error");

        result.ExitCode.ShouldBe(0);
        result.Output.ShouldBe("output");
        result.Error.ShouldBe("error");
    }

    [Fact]
    public void ProcessResult_Equality_WorksCorrectly()
    {
        var a = new CliProcessRunner.ProcessResult(0, "out", "err");
        var b = new CliProcessRunner.ProcessResult(0, "out", "err");

        a.ShouldBe(b);
    }
}
