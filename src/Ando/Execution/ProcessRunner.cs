// =============================================================================
// ProcessRunner.cs
//
// Summary: Executes commands locally as child processes with real-time output.
//
// ProcessRunner is the default ICommandExecutor implementation that runs commands
// directly on the host machine. Used in --local mode and for Docker commands.
// Provides real-time output streaming for responsive build feedback.
//
// Design Decisions:
// - Uses ProcessStartInfo.ArgumentList instead of Arguments string for safe escaping
// - Streams output in real-time rather than buffering to completion
// - Treats stderr as regular output since many tools (npm, cargo) use it for progress
// - Uses TaskCompletionSource to properly await async output completion
// - Kills entire process tree on timeout to prevent orphaned child processes
// =============================================================================

using System.Diagnostics;
using Ando.Logging;

namespace Ando.Execution;

/// <summary>
/// Executes commands locally as child processes with real-time output streaming.
/// This is the default executor used in --local mode and for Docker CLI commands.
/// </summary>
public class ProcessRunner : ICommandExecutor
{
    private readonly IBuildLogger _logger;

    public ProcessRunner(IBuildLogger logger)
    {
        _logger = logger;
    }

    public async Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null)
    {
        options ??= new CommandOptions();

        // Configure the process for captured output and no shell involvement.
        // UseShellExecute=false is required for output redirection.
        var startInfo = new ProcessStartInfo
        {
            FileName = command,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        // Using ArgumentList instead of Arguments string ensures proper escaping
        // of special characters and spaces in arguments.
        foreach (var arg in args)
        {
            startInfo.ArgumentList.Add(arg);
        }

        if (options.WorkingDirectory != null)
        {
            startInfo.WorkingDirectory = options.WorkingDirectory;
        }

        // Add environment variables from options to the process environment.
        foreach (var (key, value) in options.Environment)
        {
            startInfo.EnvironmentVariables[key] = value;
        }

        using var process = new Process { StartInfo = startInfo };

        // TaskCompletionSource ensures we wait for all output before returning.
        // Output handlers are called on thread pool threads, so we need this
        // synchronization to know when all output has been received.
        var outputComplete = new TaskCompletionSource<bool>();
        var errorComplete = new TaskCompletionSource<bool>();

        // Stream stdout lines to the logger as they arrive.
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                // Null data indicates end of stream.
                outputComplete.TrySetResult(true);
            }
            else
            {
                _logger.Info(e.Data);
            }
        };

        // Stream stderr to the logger as well.
        // Many tools use stderr for progress output, so we treat it as info.
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                errorComplete.TrySetResult(true);
            }
            else
            {
                _logger.Info(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Handle timeout if specified.
            if (options.TimeoutMs.HasValue)
            {
                var completed = await Task.WhenAny(
                    process.WaitForExitAsync(),
                    Task.Delay(options.TimeoutMs.Value)
                );

                if (!process.HasExited)
                {
                    // Kill entire process tree to prevent orphaned child processes.
                    process.Kill(entireProcessTree: true);
                    return CommandResult.Failed(-1, "Command timed out");
                }
            }
            else
            {
                await process.WaitForExitAsync();
            }

            // Wait for all output to be captured before returning.
            // The process can exit before all output is flushed.
            await Task.WhenAll(outputComplete.Task, errorComplete.Task);

            return process.ExitCode == 0
                ? CommandResult.Ok()
                : CommandResult.Failed(process.ExitCode);
        }
        catch (Exception ex)
        {
            return CommandResult.Failed(-1, ex.Message);
        }
    }

    /// <summary>
    /// Checks if a command is available by running it with --version.
    /// Most CLI tools support --version and exit with code 0.
    /// </summary>
    public bool IsAvailable(string command)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            // 5 second timeout to prevent hanging on unresponsive commands.
            process.WaitForExit(5000);
            return process.ExitCode == 0;
        }
        catch
        {
            // Command not found or other error.
            return false;
        }
    }
}
