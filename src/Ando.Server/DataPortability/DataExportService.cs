// =============================================================================
// DataExportService.cs
//
// Summary: Writes every Ando table (not Hangfire) of the current database to a
// JSON Lines export file that DataImportService can load into a database of
// either provider.
//
// Design Decisions:
// - Rows are streamed (AsNoTracking + AsAsyncEnumerable, ordered by key) and
//   written line by line, so BuildLogEntries is never materialized
// - The file is written to "<output>.partial" and renamed on success, so a
//   failed export never leaves a file that looks complete; the "__end" line is
//   an additional guard against truncation during copy
// - Refuses to export a database with pending migrations (or migrations this
//   build does not know): the CLI mode does not migrate, and the model
//   fingerprint would not describe the data
// - Command timeout is disabled: SqlClient applies it cumulatively to reading
//   a result set, so streaming a large log table would otherwise time out
// - The source should not be written to during export (stop the server); no
//   snapshot transaction is used because SQL Server snapshot isolation is not
//   enabled on existing databases
// =============================================================================

using System.Diagnostics;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Ando.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.DataPortability;

/// <summary>
/// Exports the application data to an export file.
/// </summary>
public sealed class DataExportService
{
    private readonly AndoDbContext _db;

    /// <summary>
    /// Creates the service.
    /// </summary>
    /// <param name="db">Context of the source database.</param>
    public DataExportService(AndoDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Writer options for every line: compact, readable non-ASCII text.
    /// </summary>
    internal static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Exports all tables to <paramref name="outputPath"/>.
    /// </summary>
    /// <param name="outputPath">Destination file.</param>
    /// <param name="overwrite">Replace an existing file.</param>
    /// <param name="progress">Optional callback invoked when a table is done (name, rows).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The manifest and row counts.</returns>
    /// <exception cref="DataPortabilityException">Thrown for operator-correctable problems.</exception>
    public async Task<DataExportResult> ExportAsync(
        string outputPath,
        bool overwrite,
        Action<string, long>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var fullPath = Path.GetFullPath(outputPath);
        if (File.Exists(fullPath) && !overwrite)
        {
            throw new DataPortabilityException($"Output file '{fullPath}' already exists. Use --overwrite to replace it.");
        }

        var tables = PortableTables.Resolve(_db.Model);
        var lastMigration = await RequireFullyMigratedAsync(_db, "Source", cancellationToken);

        var manifest = new ExportManifest
        {
            Format = ExportManifest.FormatName,
            FormatVersion = ExportManifest.CurrentFormatVersion,
            AppVersion = GetAppVersion(),
            // Whole seconds: the value is also used as FinishedAt of builds cancelled by
            // the import, and must survive any provider's timestamp precision unchanged.
            ExportedAtUtc = TruncateToSeconds(DateTime.UtcNow),
            SourceProvider = GetProviderName(_db),
            LastMigration = lastMigration,
            LastMigrationName = ExportManifest.MigrationName(lastMigration),
            ModelFingerprint = ModelFingerprint.Compute(tables),
            Tables = tables.Select(t => t.Name).ToList()
        };

        _db.Database.SetCommandTimeout(0);

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var partialPath = fullPath + ".partial";
        var counts = new List<KeyValuePair<string, long>>();
        try
        {
            await using (var stream = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, useAsync: true))
            await using (var writer = new Utf8JsonWriter(stream, WriterOptions))
            {
                JsonSerializer.Serialize(writer, manifest, ExportManifest.JsonOptions);
                await EndLineAsync(writer, stream, cancellationToken);

                foreach (var table in tables)
                {
                    long rows = 0;
                    await foreach (var entity in table.QueryOrdered(_db, cancellationToken))
                    {
                        WriteRow(writer, table, entity);
                        await EndLineAsync(writer, stream, cancellationToken);
                        rows++;
                    }

                    counts.Add(new KeyValuePair<string, long>(table.Name, rows));
                    progress?.Invoke(table.Name, rows);
                }

                var end = new ExportEndMarker { RowCounts = counts.ToDictionary(c => c.Key, c => c.Value) };
                JsonSerializer.Serialize(writer, end, ExportManifest.JsonOptions);
                await EndLineAsync(writer, stream, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(partialPath, fullPath, overwrite: true);
        }
        catch
        {
            TryDelete(partialPath);
            throw;
        }

        return new DataExportResult(manifest, counts, stopwatch.Elapsed);
    }

    /// <summary>
    /// Writes one row line (without the trailing newline).
    /// </summary>
    /// <param name="writer">The JSON writer.</param>
    /// <param name="table">The table the entity belongs to.</param>
    /// <param name="entity">The entity.</param>
    internal static void WriteRow(Utf8JsonWriter writer, PortableTable table, object entity)
    {
        writer.WriteStartObject();
        writer.WriteString("table", table.Name);
        writer.WriteStartObject("row");
        foreach (var property in table.Properties)
        {
            writer.WritePropertyName(property.Name);
            PortableValueSerializer.Write(writer, property.GetGetter().GetClrValue(entity));
        }

        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Verifies the database has every migration of this build applied and no unknown ones.
    /// </summary>
    /// <param name="db">The context.</param>
    /// <param name="role">"Source" or "Target", used in messages.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The last applied migration ID.</returns>
    /// <exception cref="DataPortabilityException">Thrown when the database is not exactly at this build's schema.</exception>
    internal static async Task<string> RequireFullyMigratedAsync(AndoDbContext db, string role, CancellationToken cancellationToken)
    {
        var applied = (await db.Database.GetAppliedMigrationsAsync(cancellationToken)).ToList();
        var pending = (await db.Database.GetPendingMigrationsAsync(cancellationToken)).ToList();
        var known = db.Database.GetMigrations().ToHashSet(StringComparer.Ordinal);

        if (applied.Count == 0)
        {
            throw new DataPortabilityException(
                $"{role} database has no migrations applied. Start Ando.Server once against it so it creates the schema, stop it, then retry.");
        }

        if (pending.Count > 0)
        {
            throw new DataPortabilityException(
                $"{role} database has pending migrations ({string.Join(", ", pending)}). " +
                "Start this Ando.Server version once against it so it migrates, stop it, then retry.");
        }

        var unknown = applied.Where(m => !known.Contains(m)).ToList();
        if (unknown.Count > 0)
        {
            throw new DataPortabilityException(
                $"{role} database has migrations this Ando.Server version does not know ({string.Join(", ", unknown)}); " +
                "it was migrated by a newer version. Use the same Ando.Server version on both sides.");
        }

        return applied[^1];
    }

    /// <summary>
    /// Returns the provider name used in the manifest.
    /// </summary>
    /// <param name="db">The context.</param>
    /// <returns>"SqlServer", "PostgreSql", or the EF provider name.</returns>
    internal static string GetProviderName(AndoDbContext db) =>
        db.Database.IsSqlServer() ? "SqlServer"
        : db.Database.IsNpgsql() ? "PostgreSql"
        : db.Database.ProviderName ?? "unknown";

    private static DateTime TruncateToSeconds(DateTime value) =>
        new(value.Ticks - value.Ticks % TimeSpan.TicksPerSecond, value.Kind);

    private static string GetAppVersion() =>
        typeof(DataExportService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? typeof(DataExportService).Assembly.GetName().Version?.ToString()
        ?? "unknown";

    private static async Task EndLineAsync(Utf8JsonWriter writer, Stream stream, CancellationToken cancellationToken)
    {
        await writer.FlushAsync(cancellationToken);
        stream.WriteByte((byte)'\n');
        writer.Reset();
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort; the .partial suffix already marks the file as incomplete.
        }
    }
}
