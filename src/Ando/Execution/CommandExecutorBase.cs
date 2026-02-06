// =============================================================================
// CommandExecutorBase.cs
//
// Summary: Base class for command executors with shared process execution logic.
//
// This abstract class extracts the common process execution code shared between
// ProcessRunner (local execution) and ContainerExecutor (Docker execution).
// Subclasses only need to implement command preparation and availability checking.
// =============================================================================

using System.Diagnostics;
using Ando.Logging;

namespace Ando.Execution;

/// <summary>
/// Base class for command executors that provides shared process execution logic.
/// Subclasses implement <see cref="PrepareProcessStartInfo"/> to customize command setup.
/// </summary>
public abstract class CommandExecutorBase : ICommandExecutor
{
    protected readonly IBuildLogger Logger;

    protected CommandExecutorBase(IBuildLogger logger)
    {
        Logger = logger;
    }

    public async Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null)
    {
        options ??= new CommandOptions();

        // Log the command before execution for debugging visibility.
        LogCommand(command, args, options);

        var startInfo = PrepareProcessStartInfo(command, args, options);

        // Interactive mode: let the process inherit console streams for user input.
        // Non-interactive mode: redirect streams for logging and output capture.
        if (options.Interactive)
        {
            return await ExecuteInteractiveAsync(startInfo, options);
        }
        else
        {
            return await ExecuteWithRedirectionAsync(startInfo, options);
        }
    }

    /// <summary>
    /// Executes a command in interactive mode where the process inherits console streams.
    /// Used for child builds that may prompt for user input.
    /// </summary>
    private async Task<CommandResult> ExecuteInteractiveAsync(ProcessStartInfo startInfo, CommandOptions options)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = false;
        startInfo.RedirectStandardError = false;
        startInfo.RedirectStandardInput = false;

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();

            // Handle timeout. NoTimeout (-1) means wait indefinitely.
            if (options.TimeoutMs != CommandOptions.NoTimeout)
            {
                await Task.WhenAny(
                    process.WaitForExitAsync(),
                    Task.Delay(options.TimeoutMs)
                );

                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    return CommandResult.Failed(-1, $"Command timed out after {options.TimeoutMs}ms");
                }
            }
            else
            {
                await process.WaitForExitAsync();
            }

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
    /// Executes a command with stream redirection for logging and output capture.
    /// Standard execution mode for most build commands.
    /// </summary>
    private async Task<CommandResult> ExecuteWithRedirectionAsync(ProcessStartInfo startInfo, CommandOptions options)
    {
        startInfo.UseShellExecute = false;
        startInfo.RedirectStandardOutput = true;
        startInfo.RedirectStandardError = true;
        startInfo.CreateNoWindow = true;

        using var process = new Process { StartInfo = startInfo };

        // TaskCompletionSource ensures we wait for all output before returning.
        // Output handlers are called on thread pool threads, so we need this
        // synchronization to know when all output has been received.
        var outputComplete = new TaskCompletionSource<bool>();
        var errorComplete = new TaskCompletionSource<bool>();

        // Capture stdout for commands that need to process output (e.g., Azure deployments).
        var outputBuilder = new System.Text.StringBuilder();

        // Stream stdout lines to the logger as they arrive.
        // When SuppressOutput is true, capture output but don't log it.
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                outputComplete.TrySetResult(true);
            }
            else
            {
                if (!options.SuppressOutput)
                    Logger.Info(e.Data);
                outputBuilder.AppendLine(e.Data);
            }
        };

        // Stream stderr to the logger as well.
        // Many tools use stderr for progress output, so we treat it as info.
        // When SuppressOutput is true, discard stderr silently.
        process.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                errorComplete.TrySetResult(true);
            }
            else
            {
                if (!options.SuppressOutput)
                    Logger.Info(e.Data);
            }
        };

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Handle timeout. NoTimeout (-1) means wait indefinitely.
            if (options.TimeoutMs != CommandOptions.NoTimeout)
            {
                await Task.WhenAny(
                    process.WaitForExitAsync(),
                    Task.Delay(options.TimeoutMs)
                );

                if (!process.HasExited)
                {
                    // Kill entire process tree to prevent orphaned child processes.
                    process.Kill(entireProcessTree: true);

                    // Wait briefly for any buffered output to be captured before returning.
                    await Task.Delay(100);
                    var timeoutOutput = outputBuilder.ToString().TrimEnd();
                    return CommandResult.Failed(-1, $"Command timed out after {options.TimeoutMs}ms", timeoutOutput);
                }
            }
            else
            {
                await process.WaitForExitAsync();
            }

            // Give the output handlers a moment to receive any final buffered data.
            // Docker exec may have slight delays in forwarding output from the container.
            await Task.Delay(100);

            // Wait for all output to be captured before returning.
            // The process can exit before all output is flushed, especially with
            // docker exec which may have buffering differences.
            // Use a timeout to prevent hanging forever if EOF is never received.
            var outputWaitTask = Task.WhenAll(outputComplete.Task, errorComplete.Task);
            var completedInTime = await Task.WhenAny(outputWaitTask, Task.Delay(5000)) == outputWaitTask;

            if (!completedInTime)
            {
                // Output handlers didn't complete - this can happen with docker exec
                // when the container process exits abruptly. Log a warning but continue.
                Logger.Debug("Warning: Output capture timed out, some output may be missing");
            }

            var output = outputBuilder.ToString().TrimEnd();
            return process.ExitCode == 0
                ? CommandResult.Ok(output)
                : CommandResult.Failed(process.ExitCode, output: output);
        }
        catch (Exception ex)
        {
            // Include any captured output even when an exception occurs.
            var exceptionOutput = outputBuilder.ToString().TrimEnd();
            return CommandResult.Failed(-1, ex.Message, string.IsNullOrEmpty(exceptionOutput) ? null : exceptionOutput);
        }
    }

    /// <summary>
    /// Prepares the ProcessStartInfo for command execution.
    /// Subclasses override this to customize the command setup (e.g., wrapping in docker exec).
    /// </summary>
    protected abstract ProcessStartInfo PrepareProcessStartInfo(string command, string[] args, CommandOptions options);

    public abstract bool IsAvailable(string command);

    /// <summary>
    /// Logs the command being executed for debugging and troubleshooting.
    /// This helps users see exactly what commands are run and reproduce issues.
    /// </summary>
    protected virtual void LogCommand(string command, string[] args, CommandOptions options)
    {
        // Skip debug logging when output is suppressed (e.g., internal availability checks).
        if (options.SuppressOutput)
            return;

        // Build the full command string for logging
        var escapedArgs = args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a);
        var fullCommand = $"{command} {string.Join(" ", escapedArgs)}";

        Logger.Debug($"Executing: {fullCommand}");

        if (options.WorkingDirectory != null)
        {
            Logger.Debug($"  Working directory: {options.WorkingDirectory}");
        }

        if (options.Environment.Count > 0)
        {
            Logger.Debug($"  Environment: {string.Join(", ", options.Environment.Keys)}");
        }

        if (options.TimeoutMs != CommandOptions.NoTimeout)
        {
            Logger.Debug($"  Timeout: {options.TimeoutMs}ms");
        }
    }
}
