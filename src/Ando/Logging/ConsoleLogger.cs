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
/// Supports indentation for nested builds via ANDO_INDENT_LEVEL environment variable.
/// </summary>
public class ConsoleLogger : IBuildLogger, IDisposable
{
    private readonly bool _useColor;
    private readonly StreamWriter? _logFile;
    private readonly HashSet<string> _secretValues;
    private readonly int _indentLevel;
    private readonly string _indentPrefix;
    private int _currentStep;
    private int _totalSteps;
    private bool _disposed;

    // Environment variable for tracking nested build depth.
    public const string IndentLevelEnvVar = "ANDO_INDENT_LEVEL";

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
    /// Gets the current indent level (0 for top-level, 1+ for nested builds).
    /// </summary>
    public int IndentLevel => _indentLevel;

    /// <summary>
    /// Creates a new console logger.
    /// </summary>
    /// <param name="useColor">Whether to use ANSI color codes in output.</param>
    /// <param name="logFilePath">Optional path for plain-text log file.</param>
    /// <param name="secretValues">Optional set of secret values to redact from output.</param>
    public ConsoleLogger(bool useColor = true, string? logFilePath = null, IEnumerable<string>? secretValues = null)
    {
        _useColor = useColor;
        _secretValues = secretValues != null ? [..secretValues] : [];

        // Read indent level from environment variable (set by parent build).
        var indentStr = Environment.GetEnvironmentVariable(IndentLevelEnvVar);
        _indentLevel = int.TryParse(indentStr, out var level) ? level : 0;

        // Build indent prefix: vertical line with spacing for nested builds.
        // Each level adds "│  " (vertical line + 2 spaces).
        _indentPrefix = _indentLevel > 0
            ? string.Concat(Enumerable.Repeat($"{Gray}│{Reset}  ", _indentLevel))
            : "";

        // Set up log file if path provided.
        // File is cleared on each run to avoid growing indefinitely.
        if (logFilePath != null)
        {
            _logFile = new StreamWriter(logFilePath, append: false) { AutoFlush = true };
            LogToFile($"Build started at {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            LogToFile(new string('-', 60));
        }
    }

    /// <summary>
    /// Adds secret values to be redacted from output.
    /// Can be called after construction when secrets become known.
    /// </summary>
    public void AddSecrets(IEnumerable<string> secrets)
    {
        foreach (var secret in secrets.Where(s => !string.IsNullOrEmpty(s)))
        {
            _secretValues.Add(secret);
        }
    }

    /// <summary>
    /// Redacts any secret values from the message.
    /// Replaces occurrences of secret values with [REDACTED].
    /// </summary>
    private string RedactSecrets(string message)
    {
        if (_secretValues.Count == 0) return message;

        var result = message;
        foreach (var secret in _secretValues.Where(s => s.Length >= 4)) // Only redact secrets 4+ chars
        {
            result = result.Replace(secret, "[REDACTED]");
        }
        return result;
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
        Console.Write($"{_indentPrefix}  {Cyan}▶{Reset}  {Gray}{stepCounter}{Reset}{stepName}{contextPart}");
        Console.WriteLine();
    }

    public void StepCompleted(string stepName, TimeSpan duration, string? context = null)
    {
        var contextPart = context != null ? $" ({context})" : "";

        LogToFile($"✔ {stepName}{contextPart}  {FormatDuration(duration)}");

        if (Verbosity == LogLevel.Quiet) return;

        Console.WriteLine($"{_indentPrefix}  {Green}✔{Reset}  {stepName}{contextPart}  {Gray}{FormatDuration(duration)}{Reset}");
    }

    public void StepFailed(string stepName, TimeSpan duration, string? message = null)
    {
        LogToFile($"✖ {stepName}  {FormatDuration(duration)}");
        if (message != null)
        {
            LogToFile($"  Error: {message}");
        }

        Console.WriteLine($"{_indentPrefix}  {Red}✖{Reset}  {stepName}  {Gray}{FormatDuration(duration)}{Reset}");

        if (message != null)
        {
            Console.WriteLine($"{_indentPrefix}     {Red}{message}{Reset}");
        }
    }

    public void StepSkipped(string stepName, string? reason = null)
    {
        var reasonPart = reason != null ? $" ({reason})" : "";
        LogToFile($"⊘ {stepName} skipped{reasonPart}");

        if (Verbosity < LogLevel.Normal) return;

        Console.WriteLine();
        Console.Write($"{_indentPrefix}  {Yellow}⊘{Reset}  {stepName} skipped");
        if (reason != null)
        {
            Console.Write($"  {Gray}({reason}){Reset}");
        }
        Console.WriteLine();
    }

    public void LogStep(string level, string message)
    {
        _currentStep++;
        var stepCounter = _totalSteps > 0 ? $"[{_currentStep}/{_totalSteps}] " : "";
        var redacted = RedactSecrets(message);

        LogToFile($"▶ {stepCounter}{level}: {redacted}");

        if (Verbosity == LogLevel.Quiet) return;

        // Choose color based on level.
        var levelColor = level switch
        {
            "Warning" => Yellow,
            "Error" => Red,
            "Debug" => Gray,
            _ => Cyan // Info
        };

        Console.WriteLine();
        Console.WriteLine($"{_indentPrefix}  {levelColor}▶{Reset}  {Gray}{stepCounter}{Reset}{level}: {LightGray}{redacted}{Reset}");
    }

    public void Info(string message)
    {
        var redacted = RedactSecrets(message);
        LogToFile($"  {redacted}");

        if (Verbosity < LogLevel.Normal) return;
        Console.WriteLine($"{_indentPrefix}     {LightGray}{redacted}{Reset}");
    }

    public void Warning(string message)
    {
        var redacted = RedactSecrets(message);
        LogToFile($"⚠ {redacted}");

        if (Verbosity < LogLevel.Minimal) return;
        Console.WriteLine($"{_indentPrefix}  {Yellow}⚠{Reset}  {redacted}");
    }

    public void Error(string message)
    {
        var redacted = RedactSecrets(message);
        LogToFile($"✖ ERROR: {redacted}");

        Console.WriteLine($"{_indentPrefix}  {Red}✖{Reset}  {redacted}");
    }

    public void Debug(string message)
    {
        var redacted = RedactSecrets(message);
        LogToFile($"  → {redacted}");

        if (Verbosity < LogLevel.Detailed) return;
        Console.WriteLine($"{_indentPrefix}     {Gray}→{Reset} {redacted}");
    }

    public void WorkflowStarted(string workflowName, string? scriptPath = null, int totalSteps = 0)
    {
        _currentStep = 0;
        _totalSteps = totalSteps;

        LogToFile("");
        LogToFile($"Workflow: {workflowName}");
        if (scriptPath != null) LogToFile($"Script:   {scriptPath}");
        if (totalSteps > 0) LogToFile($"Steps:    {totalSteps}");
        LogToFile("");

        if (Verbosity == LogLevel.Quiet) return;

        Console.WriteLine($"{_indentPrefix}{Gray}────────────────────────────────────────────────────────────{Reset}");
        if (scriptPath != null)
        {
            Console.WriteLine($"{_indentPrefix}  {Gray}Script:{Reset} {scriptPath}");
        }
    }

    public void WorkflowCompleted(string workflowName, string? scriptPath, TimeSpan duration, int stepsRun, int stepsFailed)
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
        Console.WriteLine($"{_indentPrefix}{Gray}────────────────────────────────────────────────────────────{Reset}");
        Console.WriteLine();

        if (stepsFailed > 0)
        {
            Console.WriteLine($"{_indentPrefix}  {Red}✖  FAILED{Reset}  {stepsFailed}/{stepsRun} steps failed  {Gray}{FormatDuration(duration)}{Reset}");
        }
        else
        {
            Console.WriteLine($"{_indentPrefix}  {Green}✔  SUCCESS{Reset}  {stepsRun} steps completed  {Gray}{FormatDuration(duration)}{Reset}");
        }

        if (scriptPath != null)
        {
            Console.WriteLine($"{_indentPrefix}     {Gray}{scriptPath}{Reset}");
        }

        Console.WriteLine();
        Console.WriteLine($"{_indentPrefix}{Gray}────────────────────────────────────────────────────────────{Reset}");
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
