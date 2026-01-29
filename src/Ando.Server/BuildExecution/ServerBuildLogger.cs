// =============================================================================
// ServerBuildLogger.cs
//
// Summary: Build logger that writes to database and streams via SignalR.
//
// Implements IBuildLogger from the core Ando project, capturing all build
// output and streaming it to connected clients in real-time via SignalR.
//
// Design Decisions:
// - Writes each log entry immediately to database (no batching)
// - Broadcasts via SignalR for real-time UI updates
// - Uses sequence numbers for ordering
// - Thread-safe via lock around DbContext (DbContext is not thread-safe)
// =============================================================================

using Ando.Logging;
using Ando.Server.Data;
using Ando.Server.Hubs;
using Ando.Server.Models;
using Microsoft.AspNetCore.SignalR;

// Alias to disambiguate from Microsoft.Extensions.Logging.LogLevel
using AndoLogLevel = Ando.Logging.LogLevel;

namespace Ando.Server.BuildExecution;

/// <summary>
/// Build logger that writes to database and streams via SignalR.
/// </summary>
public class ServerBuildLogger : IBuildLogger
{
    private readonly AndoDbContext _db;
    private readonly int _buildId;
    private readonly IHubContext<BuildLogHub> _hubContext;
    private readonly CancellationToken _cancellationToken;
    private int _sequence = 0;
    private readonly object _dbLock = new();

    public AndoLogLevel Verbosity { get; set; } = AndoLogLevel.Normal;

    public ServerBuildLogger(
        AndoDbContext db,
        int buildId,
        IHubContext<BuildLogHub> hubContext,
        CancellationToken cancellationToken)
    {
        _db = db;
        _buildId = buildId;
        _hubContext = hubContext;
        _cancellationToken = cancellationToken;
    }

    public void Info(string message)
    {
        if (Verbosity >= AndoLogLevel.Normal)
        {
            LogEntry(LogEntryType.Info, message);
        }
    }

    public void Warning(string message)
    {
        if (Verbosity >= AndoLogLevel.Minimal)
        {
            LogEntry(LogEntryType.Warning, message);
        }
    }

    public void Error(string message)
    {
        LogEntry(LogEntryType.Error, message);
    }

    public void Debug(string message)
    {
        if (Verbosity >= AndoLogLevel.Detailed)
        {
            LogEntry(LogEntryType.Debug, message);
        }
    }

    public void StepStarted(string stepName, string? context = null)
    {
        var message = context != null ? $"{stepName} ({context})" : stepName;
        LogEntry(LogEntryType.StepStarted, message, stepName);
    }

    public void StepCompleted(string stepName, TimeSpan duration, string? context = null)
    {
        var message = context != null
            ? $"{stepName} ({context}) completed in {duration.TotalSeconds:F1}s"
            : $"{stepName} completed in {duration.TotalSeconds:F1}s";
        LogEntry(LogEntryType.StepCompleted, message, stepName);
    }

    public void StepFailed(string stepName, TimeSpan duration, string? message = null)
    {
        var logMessage = string.IsNullOrEmpty(message)
            ? $"{stepName} failed after {duration.TotalSeconds:F1}s"
            : $"{stepName} failed after {duration.TotalSeconds:F1}s: {message}";
        LogEntry(LogEntryType.StepFailed, logMessage, stepName);
    }

    public void StepSkipped(string stepName, string? reason = null)
    {
        var message = string.IsNullOrEmpty(reason)
            ? $"{stepName} skipped"
            : $"{stepName} skipped: {reason}";
        LogEntry(LogEntryType.Info, message, stepName);
    }

    public void WorkflowStarted(string workflowName, string? scriptPath = null, int totalSteps = 0)
    {
        LogEntry(LogEntryType.Info, $"Starting build: {workflowName} ({totalSteps} steps)");
    }

    public void WorkflowCompleted(string workflowName, string? scriptPath, TimeSpan duration, int stepsRun, int stepsFailed)
    {
        var status = stepsFailed > 0 ? "failed" : "succeeded";
        LogEntry(LogEntryType.Info,
            $"Build {status}: {stepsRun} steps completed, {stepsFailed} failed in {duration.TotalSeconds:F1}s");
    }

    public void LogStep(string level, string message)
    {
        var type = level.ToLowerInvariant() switch
        {
            "warning" => LogEntryType.Warning,
            "error" => LogEntryType.Error,
            "debug" => LogEntryType.Debug,
            _ => LogEntryType.Info
        };
        LogEntry(type, message);
    }

    /// <summary>
    /// Logs raw command output.
    /// </summary>
    public void Output(string message)
    {
        LogEntry(LogEntryType.Output, message);
    }

    /// <summary>
    /// Creates a log entry and broadcasts it.
    /// </summary>
    private void LogEntry(LogEntryType type, string message, string? stepName = null)
    {
        var entry = new BuildLogEntry
        {
            BuildId = _buildId,
            Sequence = Interlocked.Increment(ref _sequence),
            Type = type,
            Message = message.Length > 4000 ? message[..4000] : message,
            StepName = stepName,
            Timestamp = DateTime.UtcNow
        };

        try
        {
            // Synchronize database access - DbContext is not thread-safe
            lock (_dbLock)
            {
                _db.BuildLogEntries.Add(entry);
                _db.SaveChanges();
            }

            // Broadcast to SignalR clients watching this build.
            // Use explicit camelCase property names to match JavaScript expectations.
            // Fire-and-forget with explicit discard - we don't want to block logging on SignalR.
            // Wrap in try-catch to prevent unobserved task exceptions.
            var groupName = BuildLogHub.GetGroupName(_buildId);
            _ = _hubContext.Clients.Group(groupName)
                .SendAsync("LogEntry", new
                {
                    id = entry.Id,
                    sequence = entry.Sequence,
                    type = entry.Type.ToString(),
                    message = entry.Message,
                    stepName = entry.StepName,
                    timestamp = entry.Timestamp
                }, _cancellationToken)
                .ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        // Log failure silently - SignalR delivery is best-effort
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
        }
        catch (OperationCanceledException)
        {
            // Build was cancelled, ignore
        }
    }
}
