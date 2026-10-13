// =============================================================================
// FileLoggingExtensions.cs
//
// Summary: Adds the optional Serilog rolling file sink (Logging:File) to the
// host's logging pipeline.
//
// Design Decisions:
// - Serilog is added as one more Microsoft.Extensions.Logging provider instead
//   of replacing the logger factory (UseSerilog). The console provider, the
//   Logging:LogLevel filters and the AddFilter calls in Program.cs therefore
//   apply unchanged to stdout and to the file; the Serilog logger itself
//   accepts every level and leaves filtering to Microsoft.Extensions.Logging
// - One file per day (ando-yyyyMMdd.log), RetainedDays files kept, shared so
//   another process (e.g. the export CLI or a log shipper) may open it,
//   flushed to disk every 2 seconds
// - The directory is probed before the sink is added. If it cannot be created
//   or written, startup continues without the file sink and a warning is logged
//   once the app is built (FileLoggingFallbackWarning)
// =============================================================================

using Ando.Server.Configuration;
using Serilog;
using Serilog.Events;

namespace Ando.Server.Infrastructure.Logging;

/// <summary>
/// Startup diagnostic used when file logging is enabled but the directory is not writable.
/// </summary>
/// <param name="Path">The configured log directory.</param>
/// <param name="Exception">Why the directory could not be used.</param>
internal sealed record FileLoggingFallbackWarning(string Path, Exception Exception);

/// <summary>
/// Registration of the optional file log.
/// </summary>
public static class FileLoggingExtensions
{
    /// <summary>
    /// File name pattern; Serilog inserts the date before the extension (ando-20261013.log).
    /// </summary>
    public const string FileNamePattern = "ando-.log";

    /// <summary>
    /// Output template of the file sink.
    /// </summary>
    public const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Adds the rolling file sink when Logging:File:Enabled is true and the directory is writable.
    /// </summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The builder.</returns>
    public static WebApplicationBuilder AddAndoFileLogging(this WebApplicationBuilder builder)
    {
        var settings = builder.Configuration.GetSection(FileLoggingSettings.SectionName).Get<FileLoggingSettings>()
            ?? new FileLoggingSettings();
        builder.Services.Configure<FileLoggingSettings>(builder.Configuration.GetSection(FileLoggingSettings.SectionName));

        if (!settings.Enabled)
        {
            return builder;
        }

        try
        {
            Directory.CreateDirectory(settings.Path);
            var probe = System.IO.Path.Combine(settings.Path, ".ando_write_probe");
            File.WriteAllText(probe, DateTime.UtcNow.ToString("O"));
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            builder.Services.AddSingleton(new FileLoggingFallbackWarning(settings.Path, ex));
            return builder;
        }

        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .Enrich.FromLogContext()
            .WriteTo.File(
                System.IO.Path.Combine(settings.Path, FileNamePattern),
                outputTemplate: OutputTemplate,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: settings.RetainedDays > 0 ? settings.RetainedDays : null,
                shared: true,
                flushToDiskInterval: TimeSpan.FromSeconds(2),
                restrictedToMinimumLevel: LogEventLevel.Verbose)
            .CreateLogger();

        builder.Logging.AddSerilog(logger, dispose: true);
        return builder;
    }
}
