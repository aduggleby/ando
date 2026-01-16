// =============================================================================
// TestLogger.cs
//
// Summary: Test double for IBuildLogger that captures all log events.
//
// TestLogger implements IBuildLogger to capture all log messages and events
// for verification in unit tests. Instead of writing to the console, it
// stores everything in lists that can be inspected in assertions.
//
// Usage in tests:
//   var logger = new TestLogger();
//   // ... run code that logs ...
//   Assert.That(logger.InfoMessages, Contains.Item("Expected message"));
//   Assert.That(logger.StepsCompleted.Count, Is.EqualTo(2));
//
// Design Decisions:
// - Separate lists for each message type enable specific assertions
// - AllMessages provides chronological view with type prefixes
// - Event records (StepStartedEvent, etc.) capture full event details
// - Clear() method enables test isolation
// =============================================================================

using Ando.Logging;

namespace Ando.Tests.TestFixtures;

/// <summary>
/// Test logger that captures all log events for assertions.
/// </summary>
public class TestLogger : IBuildLogger
{
    public LogLevel Verbosity { get; set; } = LogLevel.Normal;

    // Captured messages
    public List<string> InfoMessages { get; } = new();
    public List<string> WarningMessages { get; } = new();
    public List<string> ErrorMessages { get; } = new();
    public List<string> DebugMessages { get; } = new();
    public List<string> AllMessages { get; } = new();

    // Captured step events
    public List<StepStartedEvent> StepsStarted { get; } = new();
    public List<StepCompletedEvent> StepsCompleted { get; } = new();
    public List<StepFailedEvent> StepsFailed { get; } = new();
    public List<StepSkippedEvent> StepsSkipped { get; } = new();
    public List<LogStepEvent> LogSteps { get; } = new();

    // Captured workflow events
    public List<string> WorkflowsStarted { get; } = new();
    public List<WorkflowCompletedEvent> WorkflowsCompleted { get; } = new();

    public void Info(string message)
    {
        InfoMessages.Add(message);
        AllMessages.Add($"[INFO] {message}");
    }

    public void Warning(string message)
    {
        WarningMessages.Add(message);
        AllMessages.Add($"[WARN] {message}");
    }

    public void Error(string message)
    {
        ErrorMessages.Add(message);
        AllMessages.Add($"[ERROR] {message}");
    }

    public void Debug(string message)
    {
        DebugMessages.Add(message);
        AllMessages.Add($"[DEBUG] {message}");
    }

    public void StepStarted(string stepName, string? context = null)
    {
        StepsStarted.Add(new StepStartedEvent(stepName, context));
    }

    public void StepCompleted(string stepName, TimeSpan duration, string? context = null)
    {
        StepsCompleted.Add(new StepCompletedEvent(stepName, duration, context));
    }

    public void StepFailed(string stepName, TimeSpan duration, string? message = null)
    {
        StepsFailed.Add(new StepFailedEvent(stepName, duration, message));
    }

    public void StepSkipped(string stepName, string? reason = null)
    {
        StepsSkipped.Add(new StepSkippedEvent(stepName, reason));
    }

    public void LogStep(string level, string message)
    {
        LogSteps.Add(new LogStepEvent(level, message));
        AllMessages.Add($"[{level.ToUpper()}] {message}");
    }

    public void WorkflowStarted(string workflowName, string? scriptPath = null, int totalSteps = 0)
    {
        WorkflowsStarted.Add(workflowName);
    }

    public void WorkflowCompleted(string workflowName, string? scriptPath, TimeSpan duration, int stepsRun, int stepsFailed)
    {
        WorkflowsCompleted.Add(new WorkflowCompletedEvent(workflowName, scriptPath, duration, stepsRun, stepsFailed));
    }

    /// <summary>
    /// Clears all captured events. Useful for test setup.
    /// </summary>
    public void Clear()
    {
        InfoMessages.Clear();
        WarningMessages.Clear();
        ErrorMessages.Clear();
        DebugMessages.Clear();
        AllMessages.Clear();
        StepsStarted.Clear();
        StepsCompleted.Clear();
        StepsFailed.Clear();
        StepsSkipped.Clear();
        LogSteps.Clear();
        WorkflowsStarted.Clear();
        WorkflowsCompleted.Clear();
    }

    // Event records for easier assertions
    public record StepStartedEvent(string StepName, string? Context);
    public record StepCompletedEvent(string StepName, TimeSpan Duration, string? Context);
    public record StepFailedEvent(string StepName, TimeSpan Duration, string? Message);
    public record StepSkippedEvent(string StepName, string? Reason);
    public record LogStepEvent(string Level, string Message);
    public record WorkflowCompletedEvent(string WorkflowName, string? ScriptPath, TimeSpan Duration, int StepsRun, int StepsFailed);
}
