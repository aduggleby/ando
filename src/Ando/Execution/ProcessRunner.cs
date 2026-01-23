// =============================================================================
// ProcessRunner.cs
//
// Summary: Executes commands on the host machine as child processes.
//
// ProcessRunner is the ICommandExecutor implementation that runs commands
// directly on the host machine. Used for Docker CLI commands (docker run,
// docker exec, etc.) that must run on the host to manage containers.
// Provides real-time output streaming for responsive build feedback.
//
// Design Decisions:
// - Uses ProcessStartInfo.ArgumentList instead of Arguments string for safe escaping
// - Streams output in real-time rather than buffering to completion
// - Treats stderr as regular output since many tools (npm, cargo) use it for progress
// - Kills entire process tree on timeout to prevent orphaned child processes
// =============================================================================

using System.Diagnostics;
using Ando.Logging;

namespace Ando.Execution;

/// <summary>
/// Executes commands on the host machine as child processes with real-time output streaming.
/// Used for Docker CLI commands that must run on the host to manage containers.
/// </summary>
public class ProcessRunner(IBuildLogger logger) : CommandExecutorBase(logger)
{
    protected override ProcessStartInfo PrepareProcessStartInfo(string command, string[] args, CommandOptions options)
    {
        var startInfo = new ProcessStartInfo { FileName = command };

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

        return startInfo;
    }

    /// <summary>
    /// Checks if a command is available by running it with --version.
    /// Most CLI tools support --version and exit with code 0.
    /// </summary>
    public override bool IsAvailable(string command)
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
