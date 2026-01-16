// =============================================================================
// LogOperations.cs
//
// Summary: Provides logging operations for build scripts.
//
// LogOperations allows build scripts to output messages at different log levels.
// This is useful for debugging, providing progress information, and displaying
// custom messages during build execution.
//
// Usage in build.csando:
//   Log.Info("Starting deployment...");
//   Log.Warning("Cache is stale, rebuilding");
//   Log.Error("Failed to connect to server");
//   Log.Debug("Connection string: ...");
//
// Design Decisions:
// - Registers log steps that render as single lines: "▶ [1/5] Info: message"
// - Exposes standard log levels: Info, Warning, Error, Debug
// - Steps execute in order with other build steps
// =============================================================================

using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Provides logging operations for build scripts.
/// Allows scripts to output messages at different log levels.
/// Logs are registered as special log steps that render as single lines.
/// </summary>
public class LogOperations
{
    private readonly IStepRegistry _registry;

    public LogOperations(IStepRegistry registry)
    {
        _registry = registry;
    }

    /// <summary>
    /// Logs an informational message.
    /// Visible at Normal and Detailed verbosity levels.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Info(string message)
    {
        _registry.Register(new BuildStep("Log.Info", () => Task.FromResult(true))
        {
            IsLogStep = true,
            LogLevel = LogStepLevel.Info,
            LogMessage = message
        });
    }

    /// <summary>
    /// Logs a warning message.
    /// Visible at Minimal, Normal, and Detailed verbosity levels.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Warning(string message)
    {
        _registry.Register(new BuildStep("Log.Warning", () => Task.FromResult(true))
        {
            IsLogStep = true,
            LogLevel = LogStepLevel.Warning,
            LogMessage = message
        });
    }

    /// <summary>
    /// Logs an error message.
    /// Always visible regardless of verbosity level.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Error(string message)
    {
        _registry.Register(new BuildStep("Log.Error", () => Task.FromResult(true))
        {
            IsLogStep = true,
            LogLevel = LogStepLevel.Error,
            LogMessage = message
        });
    }

    /// <summary>
    /// Logs a debug message.
    /// Only visible at Detailed verbosity level.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Debug(string message)
    {
        _registry.Register(new BuildStep("Log.Debug", () => Task.FromResult(true))
        {
            IsLogStep = true,
            LogLevel = LogStepLevel.Debug,
            LogMessage = message
        });
    }
}
