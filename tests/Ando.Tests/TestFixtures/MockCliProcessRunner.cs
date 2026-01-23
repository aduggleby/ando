// =============================================================================
// MockCliProcessRunner.cs
//
// Summary: Test double for CliProcessRunner that captures commands without execution.
//
// MockCliProcessRunner provides a controllable implementation for unit testing
// CLI commands. Instead of executing actual processes, it records them for later
// assertion and returns configurable results.
//
// Design Decisions:
// - Mirrors CliProcessRunner API for drop-in replacement
// - Records all executed commands for verification
// - Supports configurable outputs per command
// - Supports failure simulation
// =============================================================================

using Ando.Utilities;

namespace Ando.Tests.TestFixtures;

/// <summary>
/// Mock process runner for testing CLI commands without executing real processes.
/// </summary>
public class MockCliProcessRunner : CliProcessRunner
{
    /// <summary>
    /// All commands that were executed, in order.
    /// </summary>
    public List<ExecutedProcess> ExecutedProcesses { get; } = [];

    /// <summary>
    /// When true, all commands return failure.
    /// </summary>
    public bool SimulateFailure { get; set; }

    /// <summary>
    /// Exit code to return when SimulateFailure is true.
    /// </summary>
    public int FailureExitCode { get; set; } = 1;

    /// <summary>
    /// Error message to return when SimulateFailure is true.
    /// </summary>
    public string? FailureMessage { get; set; }

    /// <summary>
    /// Command-specific outputs.
    /// Key format: "command args" (e.g., "git status --porcelain")
    /// </summary>
    public Dictionary<string, ProcessResult> CommandResults { get; } = new();

    /// <summary>
    /// Default output for commands without specific results configured.
    /// </summary>
    public string DefaultOutput { get; set; } = "";

    /// <summary>
    /// Sets the result for a specific command pattern.
    /// </summary>
    public void SetResult(string command, string args, ProcessResult result)
    {
        CommandResults[$"{command} {args}"] = result;
    }

    /// <summary>
    /// Sets a successful result with specific output for a command.
    /// </summary>
    public void SetOutput(string command, string args, string output)
    {
        CommandResults[$"{command} {args}"] = new ProcessResult(0, output, "");
    }

    /// <summary>
    /// Sets a failure result for a command.
    /// </summary>
    public void SetFailure(string command, string args, string error = "Command failed")
    {
        CommandResults[$"{command} {args}"] = new ProcessResult(1, "", error);
    }

    public override async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? stdin = null,
        int timeoutMs = 60000,
        string? workingDirectory = null)
    {
        var executed = new ExecutedProcess(fileName, arguments, stdin, workingDirectory);
        ExecutedProcesses.Add(executed);

        // Allow async pattern without actual delay.
        await Task.CompletedTask;

        if (SimulateFailure)
        {
            return new ProcessResult(FailureExitCode, "", FailureMessage ?? "Simulated failure");
        }

        // Look for exact match first.
        var key = $"{fileName} {arguments}";
        if (CommandResults.TryGetValue(key, out var result))
        {
            return result;
        }

        // Look for partial matches (command name only).
        var commandOnlyResult = CommandResults
            .Where(kvp => kvp.Key.StartsWith($"{fileName} "))
            .Select(kvp => kvp.Value)
            .FirstOrDefault();

        if (commandOnlyResult != null)
        {
            return commandOnlyResult;
        }

        return new ProcessResult(0, DefaultOutput, "");
    }

    public override async Task<string> RunClaudeAsync(string prompt, int timeoutMs = 120000)
    {
        var result = await RunAsync("claude", "-p --dangerously-skip-permissions", stdin: prompt, timeoutMs: timeoutMs);

        if (result.ExitCode != 0)
        {
            throw new Exception($"Claude failed: {result.Error}");
        }

        return result.Output.Trim();
    }

    /// <summary>
    /// Clears all recorded commands.
    /// </summary>
    public void Clear()
    {
        ExecutedProcesses.Clear();
    }

    /// <summary>
    /// Resets all configuration to defaults.
    /// </summary>
    public void Reset()
    {
        Clear();
        SimulateFailure = false;
        FailureExitCode = 1;
        FailureMessage = null;
        CommandResults.Clear();
        DefaultOutput = "";
    }

    /// <summary>
    /// Gets the last executed process, or null if none.
    /// </summary>
    public ExecutedProcess? LastProcess => ExecutedProcesses.Count > 0 ? ExecutedProcesses[^1] : null;

    /// <summary>
    /// Checks if a command was executed with specific arguments.
    /// </summary>
    public bool WasExecuted(string fileName, string argsContain)
    {
        return ExecutedProcesses.Any(p =>
            p.FileName == fileName &&
            p.Arguments.Contains(argsContain));
    }
}

/// <summary>
/// Record of an executed process with all details.
/// </summary>
public record ExecutedProcess(
    string FileName,
    string Arguments,
    string? Stdin,
    string? WorkingDirectory)
{
    public string CommandLine => $"{FileName} {Arguments}";
}
