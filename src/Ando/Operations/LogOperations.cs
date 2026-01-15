// =============================================================================
// LogOperations.cs
//
// Summary: Provides logging operations for build scripts.
//
// LogOperations allows build scripts to output messages at different log levels.
// This is useful for debugging, providing progress information, and displaying
// custom messages during build execution.
//
// Usage in build.ando:
//   Log.Info("Starting deployment...");
//   Log.Warning("Cache is stale, rebuilding");
//   Log.Error("Failed to connect to server");
//   Log.Debug("Connection string: ...");
//
// Design Decisions:
// - Simple wrapper around IBuildLogger for script access
// - Exposes standard log levels: Info, Warning, Error, Debug
// - No step registration needed - logs immediately during script execution
// =============================================================================

using Ando.Logging;

namespace Ando.Operations;

/// <summary>
/// Provides logging operations for build scripts.
/// Allows scripts to output messages at different log levels.
/// </summary>
public class LogOperations
{
    private readonly IBuildLogger _logger;

    public LogOperations(IBuildLogger logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Logs an informational message.
    /// Visible at Normal and Detailed verbosity levels.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Info(string message)
    {
        _logger.Info(message);
    }

    /// <summary>
    /// Logs a warning message.
    /// Visible at Minimal, Normal, and Detailed verbosity levels.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Warning(string message)
    {
        _logger.Warning(message);
    }

    /// <summary>
    /// Logs an error message.
    /// Always visible regardless of verbosity level.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Error(string message)
    {
        _logger.Error(message);
    }

    /// <summary>
    /// Logs a debug message.
    /// Only visible at Detailed verbosity level.
    /// </summary>
    /// <param name="message">The message to log.</param>
    public void Debug(string message)
    {
        _logger.Debug(message);
    }
}
