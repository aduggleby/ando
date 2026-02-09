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
using Ando.Config;

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
        // Allow override for environments where "claude" isn't on PATH (common on Windows).
        // Priority: env var, then ando.config, then "claude".
        var configuredClaude = Environment.GetEnvironmentVariable("ANDO_CLAUDE");
        if (string.IsNullOrWhiteSpace(configuredClaude))
        {
            try
            {
                var config = ProjectConfig.Load(Directory.GetCurrentDirectory());
                configuredClaude = config.ClaudePath;
            }
            catch
            {
                // Ignore config lookup issues and fall back to default.
            }
        }

        var claudeCommand = string.IsNullOrWhiteSpace(configuredClaude) ? "claude" : configuredClaude.Trim();
        const string claudeArgs = "-p --dangerously-skip-permissions";

        // On Windows, npm typically installs a `claude.cmd` shim. When UseShellExecute=false,
        // starting "claude" may fail (no .exe) and starting a .cmd/.bat requires cmd.exe.
        // Using cmd.exe also preserves normal PATH resolution (where.exe / PATHEXT behavior).
        var (fileName, arguments) = OperatingSystem.IsWindows()
            ? ("cmd.exe", $"/d /s /c \"{BuildWindowsCommandLine(claudeCommand, claudeArgs)}\"")
            : (claudeCommand, claudeArgs);

        var result = await RunAsync(
            fileName,
            arguments,
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

    private static string BuildWindowsCommandLine(string command, string args)
    {
        // If the command is a path with spaces, quote it. If it's a bare command (claude),
        // leave it unquoted so cmd.exe can resolve it via PATH/PATHEXT.
        var needsQuoting = command.Contains(' ') && (command.Contains('\\') || command.Contains('/'));
        var cmd = needsQuoting ? $"\"{command}\"" : command;
        return $"{cmd} {args}";
    }
}
