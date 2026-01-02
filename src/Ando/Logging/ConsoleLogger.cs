// =============================================================================
// ConsoleLogger.cs
//
// Summary: Production implementation of IBuildLogger with colorized console output.
//
// ConsoleLogger provides rich, colorized output for terminal-based build
// execution. It uses Unicode symbols and ANSI color codes for a modern build
// experience, while also writing a plain-text log file for later analysis.
//
// Architecture:
// - Dual output: console (with colors/symbols) and log file (plain text)
// - Step counter tracks progress through the build ([1/5], [2/5], etc.)
// - Verbosity controls which messages are shown
// - Color can be disabled for environments that don't support it
//
// Design Decisions:
// - Uses bold+color ANSI codes for better SSH compatibility
// - Unicode symbols (✔, ✖, ▶) for visual build status
// - Auto-flush on log file to ensure data is written immediately
// - Implements IDisposable to properly close log file
// =============================================================================

namespace Ando.Logging;

/// <summary>
/// Production logger with colorized console output and file logging.
/// Uses ANSI escape codes for colors and Unicode symbols for status indicators.
/// </summary>
public class ConsoleLogger : IBuildLogger, IDisposable
{
    private readonly bool _useColor;
    private readonly StreamWriter? _logFile;
    private int _currentStep;
    private int _totalSteps;
    private bool _disposed;

    // ANSI color codes - using bold+color combinations for better
    // visibility across different terminal types including SSH sessions.
    private string Red => _useColor ? "\u001b[1;31m" : "";
    private string Green => _useColor ? "\u001b[1;32m" : "";
    private string Yellow => _useColor ? "\u001b[1;33m" : "";
    private string Cyan => _useColor ? "\u001b[1;36m" : "";
    private string Gray => _useColor ? "\u001b[2;37m" : "";      // dim gray for timing/metadata
    private string LightGray => _useColor ? "\u001b[0;37m" : ""; // light gray for process output
    private string Reset => _useColor ? "\u001b[0m" : "";

    public LogLevel Verbosity { get; set; } = LogLevel.Normal;

    /// <summary>
    /// Creates a new console logger.
    /// </summary>
    /// <param name="useColor">Whether to use ANSI color codes in output.</param>
    /// <param name="logFilePath">Optional path for plain-text log file.</param>
    public ConsoleLogger(bool useColor = true, string? logFilePath = null)
    {
        _useColor = useColor;

        // Set up log file if path provided.
        // File is cleared on each run to avoid growing indefinitely.
        if (logFilePath != null)
        {
            _logFile = new StreamWriter(logFilePath, append: false) { AutoFlush = true };
            LogToFile($"Build started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            LogToFile(new string('-', 60));
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_logFile != null)
        {
            LogToFile(new string('-', 60));
            LogToFile($"Build finished at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            _logFile.Dispose();
        }
    }

    public void StepStarted(string stepName, string? context = null)
    {
        _currentStep++;
        var stepCounter = _totalSteps > 0 ? $"[{_currentStep}/{_totalSteps}] " : "";
        var contextPart = context != null ? $" ({context})" : "";

        LogToFile($"▶ {stepCounter}{stepName}{contextPart}");

        if (Verbosity == LogLevel.Quiet) return;

        Console.WriteLine();
        Console.Write($"  {Cyan}▶{Reset}  {Gray}{stepCounter}{Reset}{stepName}{contextPart}");
        Console.WriteLine();
    }

    public void StepCompleted(string stepName, TimeSpan duration, string? context = null)
    {
        var contextPart = context != null ? $" ({context})" : "";

        LogToFile($"✔ {stepName}{contextPart}  {FormatDuration(duration)}");

        if (Verbosity == LogLevel.Quiet) return;

        Console.WriteLine($"  {Green}✔{Reset}  {stepName}{contextPart}  {Gray}{FormatDuration(duration)}{Reset}");
    }

    public void StepFailed(string stepName, TimeSpan duration, string? message = null)
    {
        LogToFile($"✖ {stepName}  {FormatDuration(duration)}");
        if (message != null)
        {
            LogToFile($"  Error: {message}");
        }

        Console.WriteLine($"  {Red}✖{Reset}  {stepName}  {Gray}{FormatDuration(duration)}{Reset}");

        if (message != null)
        {
            Console.WriteLine($"     {Red}{message}{Reset}");
        }
    }

    public void StepSkipped(string stepName, string? reason = null)
    {
        var reasonPart = reason != null ? $" ({reason})" : "";
        LogToFile($"⊘ {stepName} skipped{reasonPart}");

        if (Verbosity < LogLevel.Normal) return;

        Console.WriteLine();
        Console.Write($"  {Yellow}⊘{Reset}  {stepName} skipped");
        if (reason != null)
        {
            Console.Write($"  {Gray}({reason}){Reset}");
        }
        Console.WriteLine();
    }

    public void Info(string message)
    {
        LogToFile($"  {message}");

        if (Verbosity < LogLevel.Normal) return;
        Console.WriteLine($"     {LightGray}{message}{Reset}");
    }

    public void Warning(string message)
    {
        LogToFile($"⚠ {message}");

        if (Verbosity < LogLevel.Minimal) return;
        Console.WriteLine($"  {Yellow}⚠{Reset}  {message}");
    }

    public void Error(string message)
    {
        LogToFile($"✖ ERROR: {message}");

        Console.WriteLine($"  {Red}✖{Reset}  {message}");
    }

    public void Debug(string message)
    {
        LogToFile($"  → {message}");

        if (Verbosity < LogLevel.Detailed) return;
        Console.WriteLine($"     {Gray}→{Reset} {message}");
    }

    public void WorkflowStarted(string workflowName, string? scriptPath = null, int totalSteps = 0)
    {
        _currentStep = 0;
        _totalSteps = totalSteps;

        LogToFile("");
        LogToFile($"Workflow: {workflowName}");
        if (scriptPath != null) LogToFile($"Script:   {Path.GetFileName(scriptPath)}");
        if (totalSteps > 0) LogToFile($"Steps:    {totalSteps}");
        LogToFile("");

        if (Verbosity == LogLevel.Quiet) return;

        Console.WriteLine($"{Gray}────────────────────────────────────────────────────────────{Reset}");
    }

    public void WorkflowCompleted(string workflowName, TimeSpan duration, int stepsRun, int stepsFailed)
    {
        LogToFile("");
        if (stepsFailed > 0)
        {
            LogToFile($"✖ FAILED  {stepsFailed}/{stepsRun} steps failed  {FormatDuration(duration)}");
        }
        else
        {
            LogToFile($"✔ SUCCESS  {stepsRun} steps completed  {FormatDuration(duration)}");
        }
        LogToFile("");

        if (Verbosity == LogLevel.Quiet) return;

        Console.WriteLine();
        Console.WriteLine($"{Gray}────────────────────────────────────────────────────────────{Reset}");
        Console.WriteLine();

        if (stepsFailed > 0)
        {
            Console.WriteLine($"  {Red}✖  FAILED{Reset}  {stepsFailed}/{stepsRun} steps failed  {Gray}{FormatDuration(duration)}{Reset}");
        }
        else
        {
            Console.WriteLine($"  {Green}✔  SUCCESS{Reset}  {stepsRun} steps completed  {Gray}{FormatDuration(duration)}{Reset}");
        }

        Console.WriteLine();
        Console.WriteLine($"{Gray}────────────────────────────────────────────────────────────{Reset}");
        Console.WriteLine();
    }

    private void LogToFile(string message)
    {
        _logFile?.WriteLine(message);
    }

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration.TotalMinutes >= 1)
        {
            return $"{(int)duration.TotalMinutes:D2}:{duration.Seconds:D2}.{duration.Milliseconds:D3}";
        }
        return $"{duration.Seconds:D2}.{duration.Milliseconds:D3}s";
    }
}
