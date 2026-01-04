// =============================================================================
// ICommandExecutor.cs
//
// Summary: Defines the abstraction for executing CLI commands in different
// environments (local or Docker container).
//
// This interface enables ANDO to execute build commands in different contexts
// without changing the build logic. The strategy pattern allows switching between
// local execution (ProcessRunner) and container execution (ContainerExecutor).
//
// Design Decisions:
// - Interface allows swapping execution strategy (local vs container)
// - CommandResult is a record for immutable result handling
// - CommandOptions uses mutable class because it's configured before execution
// - Async-first design since command execution is inherently I/O-bound
// - Real-time output streaming for build progress visibility
// =============================================================================

namespace Ando.Execution;

/// <summary>
/// Result of executing a command.
/// Immutable record to prevent accidental modification of results.
/// </summary>
public record CommandResult(
    int ExitCode,
    bool Success,
    string? Error = null,
    string? Output = null
)
{
    // Factory methods for common result cases.
    public static CommandResult Ok(string? output = null) => new(0, true, null, output);
    public static CommandResult Failed(int exitCode, string? error = null) => new(exitCode, false, error);
}

/// <summary>
/// Options for command execution.
/// Mutable class to allow fluent configuration before execution.
/// </summary>
public class CommandOptions
{
    /// <summary>
    /// Default timeout for commands in milliseconds (5 minutes).
    /// Prevents commands from hanging indefinitely.
    /// </summary>
    public const int DefaultTimeoutMs = 300_000;

    /// <summary>
    /// No timeout value - use with caution as commands may hang indefinitely.
    /// </summary>
    public const int NoTimeout = -1;

    /// <summary>
    /// Working directory for the command.
    /// If null, uses the current working directory (or container default).
    /// </summary>
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Environment variables to set for the command.
    /// Added to the process environment, not replacing it.
    /// </summary>
    public Dictionary<string, string> Environment { get; } = new();

    /// <summary>
    /// Timeout for the command in milliseconds.
    /// Defaults to <see cref="DefaultTimeoutMs"/> (5 minutes).
    /// Set to <see cref="NoTimeout"/> (-1) to disable timeout.
    /// Commands exceeding this timeout are forcefully terminated.
    /// </summary>
    public int TimeoutMs { get; set; } = DefaultTimeoutMs;
}

/// <summary>
/// Interface for executing CLI commands.
/// Implementations include ProcessRunner (local) and ContainerExecutor (Docker).
/// This abstraction enables ANDO to run the same build logic in different environments.
/// </summary>
public interface ICommandExecutor
{
    /// <summary>
    /// Executes a command with real-time output streaming.
    /// Output is streamed to the logger as it's produced by the command.
    /// </summary>
    /// <param name="command">The command to execute (e.g., "dotnet")</param>
    /// <param name="args">Command arguments</param>
    /// <param name="options">Optional execution options</param>
    /// <returns>Result containing exit code and success status</returns>
    Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null);

    /// <summary>
    /// Checks if a command/tool is available in the execution environment.
    /// Used to provide helpful error messages before attempting execution.
    /// </summary>
    bool IsAvailable(string command);
}
