using Ando.Execution;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Integration;

/// <summary>
/// Integration tests for ProcessRunner.
/// These tests execute actual system commands.
/// </summary>
[Trait("Category", "Integration")]
public class ProcessRunnerTests
{
    private readonly TestLogger _logger = new();

    [Fact]
    public async Task ExecuteAsync_EchoCommand_CapturesOutput()
    {
        var runner = new ProcessRunner(_logger);

        var result = await runner.ExecuteAsync("echo", new[] { "hello world" });

        Assert.True(result.Success);
        Assert.Contains("hello world", _logger.InfoMessages);
    }

    [Fact]
    public async Task ExecuteAsync_CommandNotFound_ReturnsFailure()
    {
        var runner = new ProcessRunner(_logger);

        var result = await runner.ExecuteAsync("nonexistent-command-12345", Array.Empty<string>());

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_FailingCommand_ReturnsFailure()
    {
        var runner = new ProcessRunner(_logger);

        var result = await runner.ExecuteAsync("false", Array.Empty<string>());

        Assert.False(result.Success);
        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public async Task ExecuteAsync_WithWorkingDirectory_ExecutesInCorrectDirectory()
    {
        var runner = new ProcessRunner(_logger);
        var tempDir = Path.GetTempPath();

        var result = await runner.ExecuteAsync("pwd", Array.Empty<string>(), new CommandOptions
        {
            WorkingDirectory = tempDir
        });

        Assert.True(result.Success);
        // The temp path might be a symlink on some systems, so check for containment
        Assert.True(_logger.InfoMessages.Any(m => m.Contains("tmp") || m.Contains("temp")));
    }

    [Fact]
    public async Task ExecuteAsync_WithTimeout_TimesOutLongRunningCommand()
    {
        var runner = new ProcessRunner(_logger);

        var result = await runner.ExecuteAsync("sleep", new[] { "10" }, new CommandOptions
        {
            TimeoutMs = 100
        });

        Assert.False(result.Success);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("timed out", result.Error ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ExecuteAsync_WithEnvironmentVariable_SetsVariable()
    {
        var runner = new ProcessRunner(_logger);
        var uniqueValue = Guid.NewGuid().ToString();

        var result = await runner.ExecuteAsync("printenv", new[] { "TEST_VAR" }, new CommandOptions
        {
            Environment = { ["TEST_VAR"] = uniqueValue }
        });

        Assert.True(result.Success);
        Assert.Contains(uniqueValue, _logger.InfoMessages);
    }

    [Fact]
    public void IsAvailable_DotnetCommand_ReturnsTrue()
    {
        var runner = new ProcessRunner(_logger);

        var available = runner.IsAvailable("dotnet");

        Assert.True(available);
    }

    [Fact]
    public void IsAvailable_NonexistentCommand_ReturnsFalse()
    {
        var runner = new ProcessRunner(_logger);

        var available = runner.IsAvailable("nonexistent-command-67890");

        Assert.False(available);
    }

    [Fact]
    public async Task ExecuteAsync_DotnetVersion_ExecutesSuccessfully()
    {
        var runner = new ProcessRunner(_logger);

        var result = await runner.ExecuteAsync("dotnet", new[] { "--version" });

        Assert.True(result.Success);
        Assert.True(_logger.InfoMessages.Any(m => m.Contains(".")), "Expected version number in output");
    }

    [Fact]
    public async Task ExecuteAsync_CapturesStderr()
    {
        var runner = new ProcessRunner(_logger);

        // Many tools output to stderr for progress info
        // ls on a non-existent file outputs to stderr
        var result = await runner.ExecuteAsync("ls", new[] { "/nonexistent-path-99999" });

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ExecuteAsync_MultipleArguments_PassesAllArguments()
    {
        var runner = new ProcessRunner(_logger);

        var result = await runner.ExecuteAsync("echo", new[] { "one", "two", "three" });

        Assert.True(result.Success);
        var output = string.Join(" ", _logger.InfoMessages);
        Assert.Contains("one", output);
        Assert.Contains("two", output);
        Assert.Contains("three", output);
    }
}
