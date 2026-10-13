// =============================================================================
// ExportManifest.cs
//
// Summary: Records that describe the export file: the manifest (first line),
// the end marker (last line), and the results of an export or import run.
//
// File format (JSON Lines, UTF-8, one JSON object per line):
//   line 1:   manifest  {"format":"ando-export","formatVersion":1,...}
//   lines:    rows      {"table":"Projects","row":{...columns...}}
//             grouped by table, tables in PortableTables.TableNames order
//   last:     end       {"table":"__end","rowCounts":{"Projects":2,...}}
// A file without the end line is treated as truncated and refused.
//
// Design Decisions:
// - Compatibility is decided by ModelFingerprint, not by migration IDs: the
//   SQL Server set has six migrations while PostgreSQL starts from one squashed
//   InitialCreate, so the last migration names differ for the same schema.
//   lastMigration/lastMigrationName are kept for diagnostics
// - Property names are camelCase to match the documented format
// =============================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Ando.Server.DataPortability;

/// <summary>
/// First line of an export file.
/// </summary>
public sealed record ExportManifest
{
    /// <summary>
    /// Expected value of <see cref="Format"/>.
    /// </summary>
    public const string FormatName = "ando-export";

    /// <summary>
    /// The format version this build writes and reads.
    /// </summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>
    /// Format identifier; <see cref="FormatName"/> in a valid file. No default, so a JSON
    /// object without "format" is not mistaken for a manifest.
    /// </summary>
    public string Format { get; init; } = "";

    /// <summary>
    /// Format version; this build reads only <see cref="CurrentFormatVersion"/>.
    /// </summary>
    public int FormatVersion { get; init; }

    /// <summary>
    /// Ando.Server version that wrote the file.
    /// </summary>
    public string AppVersion { get; init; } = "";

    /// <summary>
    /// When the export started (UTC).
    /// </summary>
    public DateTime ExportedAtUtc { get; init; }

    /// <summary>
    /// Database provider of the source instance (SqlServer or PostgreSql).
    /// </summary>
    public string SourceProvider { get; init; } = "";

    /// <summary>
    /// Last applied migration ID in the source database (provider-specific).
    /// </summary>
    public string LastMigration { get; init; } = "";

    /// <summary>
    /// Name part of <see cref="LastMigration"/> after the timestamp underscore.
    /// </summary>
    public string LastMigrationName { get; init; } = "";

    /// <summary>
    /// Provider-neutral fingerprint of the exported tables and columns (see <see cref="ModelFingerprint"/>).
    /// </summary>
    public string ModelFingerprint { get; init; } = "";

    /// <summary>
    /// Table names in the order their rows appear in the file.
    /// </summary>
    public IReadOnlyList<string> Tables { get; init; } = [];

    /// <summary>
    /// JSON options for the manifest and end marker lines.
    /// </summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// Extracts the name part of a migration ID ("20261013075421_AddX" becomes "AddX").
    /// </summary>
    /// <param name="migrationId">A migration ID, or empty.</param>
    /// <returns>The part after the first underscore, or the whole ID when it has none.</returns>
    public static string MigrationName(string migrationId)
    {
        var index = migrationId.IndexOf('_');
        return index >= 0 ? migrationId[(index + 1)..] : migrationId;
    }
}

/// <summary>
/// Last line of an export file; its presence proves the file is complete.
/// </summary>
public sealed record ExportEndMarker
{
    /// <summary>
    /// Reserved table value of the end line.
    /// </summary>
    public const string TableName = "__end";

    /// <summary>
    /// Always <see cref="TableName"/>.
    /// </summary>
    public string Table { get; init; } = TableName;

    /// <summary>
    /// Number of rows written per table.
    /// </summary>
    public IReadOnlyDictionary<string, long> RowCounts { get; init; } = new Dictionary<string, long>();
}

/// <summary>
/// Outcome of an export run.
/// </summary>
/// <param name="Manifest">The manifest written to the file.</param>
/// <param name="RowCounts">Rows written per table, in export order.</param>
/// <param name="Duration">Elapsed time.</param>
public sealed record DataExportResult(
    ExportManifest Manifest,
    IReadOnlyList<KeyValuePair<string, long>> RowCounts,
    TimeSpan Duration);

/// <summary>
/// Outcome of an import run.
/// </summary>
/// <param name="Manifest">The manifest read from the file.</param>
/// <param name="RowCounts">Rows inserted per table, in import order (verified against the file).</param>
/// <param name="DeletedRows">Rows deleted from the target before inserting (seed data, or everything with --force).</param>
/// <param name="CancelledBuilds">Builds that were Queued or Running in the export and were imported as Cancelled.</param>
/// <param name="SecretsVerified">Project secrets decrypted successfully with the target's encryption key.</param>
/// <param name="Duration">Elapsed time.</param>
public sealed record DataImportResult(
    ExportManifest Manifest,
    IReadOnlyList<KeyValuePair<string, long>> RowCounts,
    long DeletedRows,
    int CancelledBuilds,
    int SecretsVerified,
    TimeSpan Duration);

/// <summary>
/// A failure with a message meant for the operator (no stack trace needed).
/// </summary>
public sealed class DataPortabilityException : Exception
{
    /// <summary>
    /// Creates the exception.
    /// </summary>
    /// <param name="message">Operator-facing message.</param>
    /// <param name="innerException">Optional cause.</param>
    public DataPortabilityException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
