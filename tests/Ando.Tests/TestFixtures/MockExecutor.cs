// =============================================================================
// MockExecutor.cs
//
// Summary: Test double for ICommandExecutor that captures commands without execution.
//
// MockExecutor provides a controllable implementation of ICommandExecutor for
// unit testing. Instead of executing actual commands, it records them for later
// assertion and returns configurable results.
//
// Usage in tests:
//   var executor = new MockExecutor();
//   executor.SimulateFailure = true;  // Make commands fail
//   // ... run code that uses executor ...
//   Assert.That(executor.WasExecuted("dotnet", "build"));
//
// Design Decisions:
// - Records all executed commands for verification in assertions
// - Supports multiple failure modes: global, per-command, conditional
// - ExecutedCommand record provides rich assertion helpers
// - Clear() and Reset() enable test isolation
// =============================================================================

using Ando.Execution;

namespace Ando.Tests.TestFixtures;

/// <summary>
/// Mock executor for testing that captures commands without executing them.
/// Supports configurable failure scenarios and command-specific behavior.
/// </summary>
public class MockExecutor : ICommandExecutor
{
    /// <summary>
    /// All commands that were executed, in order.
    /// </summary>
    public List<ExecutedCommand> ExecutedCommands { get; } = new();

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
    /// Commands that should fail (by command name).
    /// </summary>
    public HashSet<string> FailingCommands { get; } = new();

    /// <summary>
    /// Specific command patterns that should fail.
    /// Key is the command, value is a predicate on args.
    /// </summary>
    public Dictionary<string, Func<string[], bool>> ConditionalFailures { get; } = new();

    /// <summary>
    /// Commands that are available (for IsAvailable checks).
    /// If empty, all commands are available.
    /// </summary>
    public HashSet<string> AvailableCommands { get; } = new();

    /// <summary>
    /// When true, simulate command timeout.
    /// </summary>
    public bool SimulateTimeout { get; set; }

    /// <summary>
    /// Delay to add before returning (for testing async behavior).
    /// </summary>
    public TimeSpan ExecutionDelay { get; set; } = TimeSpan.Zero;

    public async Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null)
    {
        var executed = new ExecutedCommand(command, args, options);
        ExecutedCommands.Add(executed);

        if (ExecutionDelay > TimeSpan.Zero)
        {
            await Task.Delay(ExecutionDelay);
        }

        if (SimulateTimeout)
        {
            return CommandResult.Failed(-1, "Command timed out");
        }

        if (SimulateFailure)
        {
            return CommandResult.Failed(FailureExitCode, FailureMessage);
        }

        if (FailingCommands.Contains(command))
        {
            return CommandResult.Failed(FailureExitCode, $"Command '{command}' configured to fail");
        }

        if (ConditionalFailures.TryGetValue(command, out var predicate) && predicate(args))
        {
            return CommandResult.Failed(FailureExitCode, $"Command '{command}' with args matched failure condition");
        }

        return CommandResult.Ok();
    }

    public bool IsAvailable(string command)
    {
        if (AvailableCommands.Count == 0)
        {
            return true; // All available by default
        }
        return AvailableCommands.Contains(command);
    }

    /// <summary>
    /// Clears all recorded commands. Useful between test assertions.
    /// </summary>
    public void Clear()
    {
        ExecutedCommands.Clear();
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
        FailingCommands.Clear();
        ConditionalFailures.Clear();
        AvailableCommands.Clear();
        SimulateTimeout = false;
        ExecutionDelay = TimeSpan.Zero;
    }

    /// <summary>
    /// Gets the last executed command, or null if none.
    /// </summary>
    public ExecutedCommand? LastCommand => ExecutedCommands.Count > 0 ? ExecutedCommands[^1] : null;

    /// <summary>
    /// Gets commands filtered by command name.
    /// </summary>
    public IEnumerable<ExecutedCommand> GetCommands(string command) =>
        ExecutedCommands.Where(c => c.Command == command);

    /// <summary>
    /// Asserts that a command was executed with specific arguments.
    /// </summary>
    public bool WasExecuted(string command, params string[] expectedArgs)
    {
        return ExecutedCommands.Any(c =>
            c.Command == command &&
            expectedArgs.All(arg => c.Args.Contains(arg)));
    }
}

/// <summary>
/// Record of an executed command with all details.
/// Supports tuple deconstruction for backward compatibility.
/// </summary>
public record ExecutedCommand(string Command, string[] Args, CommandOptions? Options)
{
    /// <summary>
    /// Gets the full command line as a string.
    /// </summary>
    public string CommandLine => $"{Command} {string.Join(" ", Args)}";

    /// <summary>
    /// Checks if the args contain a specific value.
    /// </summary>
    public bool HasArg(string arg) => Args.Contains(arg);

    /// <summary>
    /// Gets the value following a flag (e.g., "-c" returns "Release").
    /// </summary>
    public string? GetArgValue(string flag)
    {
        var index = Array.IndexOf(Args, flag);
        if (index >= 0 && index + 1 < Args.Length)
        {
            return Args[index + 1];
        }
        return null;
    }

    /// <summary>
    /// Deconstructs into a tuple for backward compatibility with existing tests.
    /// </summary>
    public void Deconstruct(out string command, out string[] args)
    {
        command = Command;
        args = Args;
    }
}
