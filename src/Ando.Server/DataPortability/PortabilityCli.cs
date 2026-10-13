// =============================================================================
// PortabilityCli.cs
//
// Summary: Command-line mode of the Ando.Server binary for moving all data to
// another instance: "export" writes a file, "import" loads it.
//
// Program.cs calls RunAsync before building the web host when args[0] is
// "export" or "import", so neither Kestrel, Hangfire, nor startup migrations
// run. Usage inside the server image (entrypoint "dotnet Ando.Server.dll"):
//   docker compose run --rm ando-server export --output /data/artifacts/export/ando-export.jsonl
//   docker exec <container> dotnet Ando.Server.dll import --input /data/ando-export.jsonl
//
// Design Decisions:
// - The host is a WebApplicationBuilder with no command-line args, so it reads
//   exactly the same configuration as the server (appsettings.json,
//   appsettings.{ASPNETCORE_ENVIRONMENT}.json, environment variables) while
//   the CLI options are never mistaken for configuration keys
// - Only the DbContext (via DatabaseServiceExtensions.AddAndoDbContext, the
//   same registration the server uses) and the encryption service are
//   registered; Hangfire is not
// - Logging goes to stderr at Warning and above so the summary on stdout stays
//   readable; operator errors print one "Error:" line and exit 1
// =============================================================================

using System.Globalization;
using Ando.Server.Configuration;
using Ando.Server.Data;
using Ando.Server.Infrastructure.Database;
using Ando.Server.Services;
using Microsoft.Extensions.Logging.Console;

namespace Ando.Server.DataPortability;

/// <summary>
/// The CLI sub-command.
/// </summary>
public enum PortabilityCommandKind
{
    /// <summary>
    /// Write the database to a file.
    /// </summary>
    Export,

    /// <summary>
    /// Load a file into the database.
    /// </summary>
    Import
}

/// <summary>
/// Parsed CLI arguments.
/// </summary>
/// <param name="Kind">Export or import.</param>
/// <param name="Path">Output (export) or input (import) file; null when only help was requested.</param>
/// <param name="Overwrite">Export: replace an existing file.</param>
/// <param name="Force">Import: delete existing data in a non-empty target.</param>
/// <param name="ShowHelp">Print usage and exit 0.</param>
public sealed record PortabilityCommand(
    PortabilityCommandKind Kind,
    string? Path,
    bool Overwrite,
    bool Force,
    bool ShowHelp);

/// <summary>
/// Thrown for invalid command-line arguments.
/// </summary>
public sealed class PortabilityUsageException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">What is wrong with the arguments.</param>
    public PortabilityUsageException(string message) : base(message)
    {
    }
}

/// <summary>
/// Entry point of the export/import command-line mode.
/// </summary>
public static class PortabilityCli
{
    /// <summary>
    /// Usage text printed for --help and argument errors.
    /// </summary>
    public const string Usage =
        """
        Usage:
          dotnet Ando.Server.dll export --output <path> [--overwrite]
          dotnet Ando.Server.dll import --input <path> [--force]

        export   Write all Ando data (users, roles, projects, secrets, builds, logs,
                 artifacts metadata, API tokens, settings) to a JSON Lines file.
                 --output <path>   File to write (parent directories are created).
                 --overwrite       Replace an existing file.

        import   Load an export file into this instance's database. The database must
                 be migrated by this Ando.Server version (start the server once, then
                 stop it) and empty. Everything runs in one transaction.
                 --input <path>    File to read.
                 --force           Delete all existing Ando data in the target first.

        Configuration (Database__Provider, ConnectionStrings__DefaultConnection,
        Encryption__Key) is read exactly like the server reads it. Encryption__Key
        must be identical on both instances. Stop the server while exporting or importing.
        """;

    /// <summary>
    /// Returns whether <paramref name="args"/> selects the CLI mode.
    /// </summary>
    /// <param name="args">Process arguments.</param>
    /// <returns>True when the first argument is "export" or "import".</returns>
    public static bool IsPortabilityCommand(string[] args) =>
        args.Length > 0 && args[0] is "export" or "import";

    /// <summary>
    /// Parses the arguments of the CLI mode.
    /// </summary>
    /// <param name="args">Process arguments, starting with "export" or "import".</param>
    /// <returns>The parsed command.</returns>
    /// <exception cref="PortabilityUsageException">Thrown for invalid arguments.</exception>
    public static PortabilityCommand Parse(string[] args)
    {
        if (!IsPortabilityCommand(args))
        {
            throw new PortabilityUsageException("Expected 'export' or 'import' as the first argument.");
        }

        var kind = args[0] == "export" ? PortabilityCommandKind.Export : PortabilityCommandKind.Import;
        var pathOption = kind == PortabilityCommandKind.Export ? "--output" : "--input";
        var flagOption = kind == PortabilityCommandKind.Export ? "--overwrite" : "--force";

        string? path = null;
        var flag = false;
        var help = false;

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "--help" or "-h")
            {
                help = true;
            }
            else if (arg == pathOption)
            {
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    throw new PortabilityUsageException($"{pathOption} requires a file path.");
                }

                if (path != null)
                {
                    throw new PortabilityUsageException($"{pathOption} was given more than once.");
                }

                path = args[++i];
            }
            else if (arg == flagOption)
            {
                flag = true;
            }
            else
            {
                throw new PortabilityUsageException($"Unknown argument '{arg}' for {args[0]}.");
            }
        }

        if (help)
        {
            return new PortabilityCommand(kind, path, false, false, ShowHelp: true);
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new PortabilityUsageException($"{args[0]} requires {pathOption} <path>.");
        }

        return new PortabilityCommand(
            kind,
            path,
            Overwrite: kind == PortabilityCommandKind.Export && flag,
            Force: kind == PortabilityCommandKind.Import && flag,
            ShowHelp: false);
    }

    /// <summary>
    /// Runs the CLI mode and returns the process exit code.
    /// </summary>
    /// <param name="args">Process arguments, starting with "export" or "import".</param>
    /// <param name="stdout">Summary output (defaults to <see cref="Console.Out"/>).</param>
    /// <param name="stderr">Error output (defaults to <see cref="Console.Error"/>).</param>
    /// <returns>0 on success, 1 on failure.</returns>
    public static async Task<int> RunAsync(string[] args, TextWriter? stdout = null, TextWriter? stderr = null)
    {
        stdout ??= Console.Out;
        stderr ??= Console.Error;

        PortabilityCommand command;
        try
        {
            command = Parse(args);
        }
        catch (PortabilityUsageException ex)
        {
            await stderr.WriteLineAsync($"Error: {ex.Message}");
            await stderr.WriteLineAsync();
            await stderr.WriteLineAsync(Usage);
            return 1;
        }

        if (command.ShowHelp)
        {
            await stdout.WriteLineAsync(Usage);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler onCancel = (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += onCancel;

        try
        {
            await using var app = BuildHost(out var provider, out var target);
            await using var scope = app.Services.CreateAsyncScope();
            var services = scope.ServiceProvider;

            await stdout.WriteLineAsync($"Ando data {command.Kind.ToString().ToLowerInvariant()}");
            await stdout.WriteLineAsync($"  Database: {provider} ({target})");

            if (command.Kind == PortabilityCommandKind.Export)
            {
                await stdout.WriteLineAsync($"  Output:   {Path.GetFullPath(command.Path!)}");
                var result = await services.GetRequiredService<DataExportService>()
                    .ExportAsync(command.Path!, command.Overwrite, null, cancellation.Token);
                await WriteExportSummaryAsync(stdout, result);
            }
            else
            {
                await stdout.WriteLineAsync($"  Input:    {Path.GetFullPath(command.Path!)}");
                var result = await services.GetRequiredService<DataImportService>()
                    .ImportAsync(command.Path!, command.Force, null, cancellation.Token);
                await WriteImportSummaryAsync(stdout, result);
            }

            return 0;
        }
        catch (DataPortabilityException ex)
        {
            await stderr.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            await stderr.WriteLineAsync("Error: cancelled. Nothing was committed (import) or the partial file was removed (export).");
            return 1;
        }
        catch (InvalidOperationException ex) when (ex.InnerException == null)
        {
            // Configuration problems from AddAndoDbContext/ReadValidatedConnection.
            await stderr.WriteLineAsync($"Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            await stderr.WriteLineAsync($"Error: {ex.GetType().Name}: {ex.Message}");
            await stderr.WriteLineAsync(ex.ToString());
            return 1;
        }
        finally
        {
            Console.CancelKeyPress -= onCancel;
        }
    }

    private static WebApplication BuildHost(out DatabaseProvider provider, out string target)
    {
        // No args: CLI options must not become configuration keys.
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [] });

        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Logging.AddFilter((_, _, level) => level >= LogLevel.Warning);

        var (selected, connectionString) = DatabaseServiceExtensions.ReadValidatedConnection(builder.Configuration);
        provider = selected;
        target = DatabaseConfiguration.DescribeTarget(selected, connectionString);

        builder.Services.Configure<DatabaseSettings>(s => s.Provider = selected);
        builder.Services.Configure<EncryptionSettings>(builder.Configuration.GetSection(EncryptionSettings.SectionName));
        builder.Services.AddSingleton<IEncryptionService, EncryptionService>();
        builder.Services.AddAndoDbContext(selected, connectionString);
        builder.Services.AddScoped<DataExportService>();
        builder.Services.AddScoped(sp => new DataImportService(
            sp.GetRequiredService<AndoDbContext>(),
            () => sp.GetRequiredService<IEncryptionService>()));

        return builder.Build();
    }

    private static async Task WriteExportSummaryAsync(TextWriter output, DataExportResult result)
    {
        var m = result.Manifest;
        await output.WriteLineAsync($"  Source:   {m.SourceProvider}, migration {m.LastMigration}, Ando.Server {m.AppVersion}");
        await WriteCountsAsync(output, result.RowCounts);
        await output.WriteLineAsync($"Export completed in {FormatDuration(result.Duration)}.");
    }

    private static async Task WriteImportSummaryAsync(TextWriter output, DataImportResult result)
    {
        var m = result.Manifest;
        await output.WriteLineAsync(
            $"  File:     exported {m.ExportedAtUtc.ToString("u", CultureInfo.InvariantCulture)} from {m.SourceProvider} " +
            $"(migration {m.LastMigration}, Ando.Server {m.AppVersion})");
        await WriteCountsAsync(output, result.RowCounts);
        await output.WriteLineAsync($"  Rows deleted from target first: {result.DeletedRows}");
        await output.WriteLineAsync(
            $"  Builds cancelled (were Queued/Running; Hangfire queue is not migrated): {result.CancelledBuilds}");
        await output.WriteLineAsync(result.SecretsVerified > 0
            ? $"  Secrets verified: {result.SecretsVerified} decrypted with this instance's Encryption__Key"
            : "  Secrets verified: none in the file (encryption key not checked)");
        await output.WriteLineAsync("  Row counts match the export file.");
        await output.WriteLineAsync($"Import completed in {FormatDuration(result.Duration)}.");
    }

    private static async Task WriteCountsAsync(TextWriter output, IReadOnlyList<KeyValuePair<string, long>> counts)
    {
        await output.WriteLineAsync($"  {"Table",-20} {"Rows",12}");
        foreach (var (table, rows) in counts)
        {
            await output.WriteLineAsync($"  {table,-20} {rows,12:N0}");
        }

        await output.WriteLineAsync($"  {"Total",-20} {counts.Sum(c => c.Value),12:N0}");
    }

    private static string FormatDuration(TimeSpan duration) =>
        duration.TotalSeconds < 60
            ? $"{duration.TotalSeconds:0.0}s"
            : $"{(int)duration.TotalMinutes}m {duration.Seconds}s";
}
