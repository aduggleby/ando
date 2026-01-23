// =============================================================================
// ShellOperations.cs
//
// Summary: Shell command execution for hook scripts.
//
// ShellOperations provides a simple API for running shell commands from hooks.
// Unlike the full ANDO build infrastructure, this runs commands directly on
// the host machine without Docker containerization.
//
// Design Decisions:
// - Uses Process class directly for simplicity
// - Captures both stdout and stderr
// - Returns a result object with exit code, output, and error streams
// - Provides both async and sync variants
// =============================================================================

using System.Diagnostics;

namespace Ando.Hooks;

/// <summary>
/// Result of a shell command execution.
/// </summary>
public record ShellResult(int ExitCode, string Output, string Error)
{
    /// <summary>
    /// Whether the command succeeded (exit code 0).
    /// </summary>
    public bool Success => ExitCode == 0;
}

/// <summary>
/// Shell command execution operations for hook scripts.
/// </summary>
public class ShellOperations
{
    private readonly string _workingDirectory;

    public ShellOperations(string workingDirectory)
    {
        _workingDirectory = workingDirectory;
    }

    /// <summary>
    /// Runs a shell command asynchronously.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="args">Command arguments.</param>
    /// <returns>Shell result with exit code, output, and error.</returns>
    public async Task<ShellResult> RunAsync(string command, params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = command,
            Arguments = string.Join(" ", args),
            WorkingDirectory = _workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process { StartInfo = psi };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        await process.WaitForExitAsync();

        return new ShellResult(
            process.ExitCode,
            outputBuilder.ToString().TrimEnd(),
            errorBuilder.ToString().TrimEnd());
    }

    /// <summary>
    /// Runs a shell command synchronously.
    /// </summary>
    /// <param name="command">The command to run.</param>
    /// <param name="args">Command arguments.</param>
    /// <returns>Shell result with exit code, output, and error.</returns>
    public ShellResult Run(string command, params string[] args)
    {
        return RunAsync(command, args).GetAwaiter().GetResult();
    }
}
