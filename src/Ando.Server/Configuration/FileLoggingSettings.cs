// =============================================================================
// FileLoggingSettings.cs
//
// Summary: Optional daily rolling file logs in addition to stdout.
//
// Bound from the Logging:File section (environment variables
// Logging__File__Enabled, Logging__File__Path, Logging__File__RetainedDays).
//
// Design Decisions:
// - Disabled by default: containers log to stdout and Docker keeps those logs
// - Path is a directory; files are named ando-yyyyMMdd.log, one per day
// - Levels are not configured here: Logging:LogLevel applies to the file too
// =============================================================================

namespace Ando.Server.Configuration;

/// <summary>
/// Configuration for the optional rolling file log.
/// </summary>
public class FileLoggingSettings
{
    /// <summary>
    /// Configuration section name in appsettings/environment configuration.
    /// </summary>
    public const string SectionName = "Logging:File";

    /// <summary>
    /// Writes log files when true. Default false (stdout only).
    /// </summary>
    public bool Enabled { get; set; } = false;

    /// <summary>
    /// Directory for the log files. Default /var/log/ando.
    /// </summary>
    public string Path { get; set; } = "/var/log/ando";

    /// <summary>
    /// Number of daily files to keep; older files are deleted. Default 90.
    /// </summary>
    public int RetainedDays { get; set; } = 90;
}
