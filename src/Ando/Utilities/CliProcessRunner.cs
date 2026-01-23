// =============================================================================
// CliProcessRunner.cs
//
// Summary: Executes external processes with timeout support and output capture.
//
// This class provides simple process execution for CLI commands (bump, commit,
// release). Unlike the build system's ProcessRunner which streams output for
// real-time feedback, this captures output for programmatic use.
//
// Design Decisions:
// - Returns ProcessResult record with exit code, stdout, and stderr
// - Supports stdin for passing prompts to Claude CLI
// - Timeout support with process tree killing to prevent orphans
// - RunClaudeAsync helper for consistent Claude CLI invocation
// =============================================================================

using System.Diagnostics;

namespace Ando.Utilities;

/// <summary>
/// Executes external processes with timeout support and output capture.
/// Used by CLI commands (bump, commit, release) for git, claude, and other tools.
/// </summary>
public class CliProcessRunner
{
    /// <summary>
    /// Result of a process execution.
    /// </summary>
    /// <param name="ExitCode">The process exit code (0 = success).</param>
    /// <param name="Output">Captured stdout.</param>
    /// <param name="Error">Captured stderr.</param>
    public record ProcessResult(int ExitCode, string Output, string Error);

    /// <summary>
    /// Runs a process and captures its output.
    /// </summary>
    /// <param name="fileName">The executable to run.</param>
    /// <param name="arguments">Command-line arguments.</param>
    /// <param name="stdin">Optional input to write to stdin.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 60 seconds).</param>
    /// <param name="workingDirectory">Working directory (default: current directory).</param>
    /// <param name="streamOutput">If true, streams output to console in real-time.</param>
    /// <returns>ProcessResult with exit code and captured output.</returns>
    public virtual async Task<ProcessResult> RunAsync(
        string fileName,
        string arguments,
        string? stdin = null,
        int timeoutMs = 60000,
        string? workingDirectory = null,
        bool streamOutput = false)
    {
        using var cts = new CancellationTokenSource(timeoutMs);

        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory ?? Directory.GetCurrentDirectory()
        };

        using var process = new Process { StartInfo = startInfo };

        var outputBuilder = new System.Text.StringBuilder();
        var errorBuilder = new System.Text.StringBuilder();

        if (streamOutput)
        {
            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Console.WriteLine(e.Data);
                    outputBuilder.AppendLine(e.Data);
                }
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data != null)
                {
                    Console.Error.WriteLine(e.Data);
                    errorBuilder.AppendLine(e.Data);
                }
            };
        }

        process.Start();

        if (streamOutput)
        {
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        // Write stdin if provided (e.g., prompt for Claude).
        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        if (streamOutput)
        {
            try
            {
                await process.WaitForExitAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                process.Kill(entireProcessTree: true);
                throw new TimeoutException($"Process '{fileName}' timed out after {timeoutMs}ms");
            }

            return new ProcessResult(
                process.ExitCode,
                outputBuilder.ToString(),
                errorBuilder.ToString()
            );
        }

        // Non-streaming: read stdout and stderr concurrently to avoid deadlocks.
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Kill entire process tree to prevent orphaned child processes.
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"Process '{fileName}' timed out after {timeoutMs}ms");
        }

        return new ProcessResult(
            process.ExitCode,
            await outputTask,
            await errorTask
        );
    }

    /// <summary>
    /// Runs Claude CLI with a prompt and returns the response.
    /// Uses --dangerously-skip-permissions to avoid interactive prompts.
    /// </summary>
    /// <param name="prompt">The prompt to send to Claude.</param>
    /// <param name="timeoutMs">Timeout in milliseconds (default: 2 minutes).</param>
    /// <param name="streamOutput">If true, streams output to console in real-time.</param>
    /// <returns>Claude's response text.</returns>
    /// <exception cref="Exception">Thrown if Claude CLI fails.</exception>
    public virtual async Task<string> RunClaudeAsync(string prompt, int timeoutMs = 120000, bool streamOutput = false)
    {
        var result = await RunAsync(
            "claude",
            "-p --dangerously-skip-permissions",
            stdin: prompt,
            timeoutMs: timeoutMs,
            streamOutput: streamOutput
        );

        if (result.ExitCode != 0)
        {
            var errorMessage = !string.IsNullOrWhiteSpace(result.Error)
                ? result.Error
                : result.Output;
            throw new Exception($"Claude failed: {errorMessage}");
        }

        return result.Output.Trim();
    }
}
