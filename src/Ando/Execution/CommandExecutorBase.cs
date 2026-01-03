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

        var startInfo = PrepareProcessStartInfo(command, args, options);
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
        process.OutputDataReceived += (sender, e) =>
        {
            if (e.Data == null)
            {
                outputComplete.TrySetResult(true);
            }
            else
            {
                Logger.Info(e.Data);
                outputBuilder.AppendLine(e.Data);
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
                Logger.Info(e.Data);
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
                await Task.WhenAny(
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

            var output = outputBuilder.ToString().TrimEnd();
            return process.ExitCode == 0
                ? CommandResult.Ok(output)
                : CommandResult.Failed(process.ExitCode);
        }
        catch (Exception ex)
        {
            return CommandResult.Failed(-1, ex.Message);
        }
    }

    /// <summary>
    /// Prepares the ProcessStartInfo for command execution.
    /// Subclasses override this to customize the command setup (e.g., wrapping in docker exec).
    /// </summary>
    protected abstract ProcessStartInfo PrepareProcessStartInfo(string command, string[] args, CommandOptions options);

    public abstract bool IsAvailable(string command);
}
