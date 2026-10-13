// =============================================================================
// ExportFileReader.cs
//
// Summary: Validates the structure of an export file before anything is
// written to the target database.
//
// Reads the whole file once without materializing rows: checks the manifest,
// that every row belongs to a known table, that tables appear in manifest
// order, and that the final line is the "__end" marker whose row counts match
// the rows actually present. A file cut off during copy has no end marker and
// is refused here, before the import transaction starts.
//
// Design Decisions:
// - Each line is checked to be well-formed JSON with "table" as its first
//   member (Utf8JsonReader, no DOM); column values are validated during the
//   insert pass, which runs in a transaction
// - Line numbers are 1-based in all messages so operators can inspect the file
// =============================================================================

using System.Text;
using System.Text.Json;

namespace Ando.Server.DataPortability;

/// <summary>
/// Result of scanning an export file.
/// </summary>
/// <param name="Manifest">The parsed manifest.</param>
/// <param name="RowCounts">Rows present per table (equal to the end marker's counts).</param>
public sealed record ExportFileSummary(ExportManifest Manifest, IReadOnlyDictionary<string, long> RowCounts);

/// <summary>
/// Structural validation of export files.
/// </summary>
public static class ExportFileReader
{
    /// <summary>
    /// Opens an export file for sequential reading.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <returns>A UTF-8 reader.</returns>
    public static StreamReader Open(string path) =>
        new(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, useAsync: true),
            new UTF8Encoding(false));

    /// <summary>
    /// Parses a manifest line.
    /// </summary>
    /// <param name="line">The first line of the file.</param>
    /// <returns>The manifest.</returns>
    /// <exception cref="DataPortabilityException">Thrown when the line is not a supported manifest.</exception>
    public static ExportManifest ParseManifest(string? line)
    {
        ExportManifest? manifest;
        try
        {
            manifest = string.IsNullOrWhiteSpace(line)
                ? null
                : JsonSerializer.Deserialize<ExportManifest>(line, ExportManifest.JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new DataPortabilityException($"Line 1 is not a valid export manifest: {ex.Message}", ex);
        }

        if (manifest == null || manifest.Format != ExportManifest.FormatName)
        {
            throw new DataPortabilityException(
                $"Not an Ando export file: line 1 must be a manifest with format '{ExportManifest.FormatName}'.");
        }

        if (manifest.FormatVersion != ExportManifest.CurrentFormatVersion)
        {
            throw new DataPortabilityException(
                $"Unsupported export format version {manifest.FormatVersion}; this Ando.Server reads version " +
                $"{ExportManifest.CurrentFormatVersion}. Use an Ando.Server version that matches the exporting one.");
        }

        if (!manifest.Tables.SequenceEqual(PortableTables.TableNames))
        {
            throw new DataPortabilityException(
                $"Export file lists tables [{string.Join(", ", manifest.Tables)}] but this Ando.Server expects " +
                $"[{string.Join(", ", PortableTables.TableNames)}]. Use the same Ando.Server version on both sides.");
        }

        return manifest;
    }

    /// <summary>
    /// Validates the whole file and returns the manifest and per-table row counts.
    /// </summary>
    /// <param name="path">File path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The summary.</returns>
    /// <exception cref="DataPortabilityException">Thrown when the file is missing, malformed, or truncated.</exception>
    public static async Task<ExportFileSummary> ScanAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            throw new DataPortabilityException($"Input file '{path}' does not exist.");
        }

        using var reader = Open(path);
        var manifest = ParseManifest(await reader.ReadLineAsync(cancellationToken));

        var tableIndex = manifest.Tables
            .Select((name, index) => (name, index))
            .ToDictionary(x => x.name, x => x.index, StringComparer.Ordinal);
        var counts = manifest.Tables.ToDictionary(t => t, _ => 0L, StringComparer.Ordinal);
        var currentIndex = -1;
        ExportEndMarker? end = null;
        var lineNumber = 1;

        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            lineNumber++;
            if (line.Length == 0)
            {
                continue;
            }

            if (end != null)
            {
                throw new DataPortabilityException($"Line {lineNumber}: data found after the '{ExportEndMarker.TableName}' line.");
            }

            var table = ReadTableName(line, lineNumber);
            if (table == ExportEndMarker.TableName)
            {
                end = ParseEnd(line, lineNumber);
                continue;
            }

            if (!tableIndex.TryGetValue(table, out var index))
            {
                throw new DataPortabilityException($"Line {lineNumber}: unknown table '{table}'.");
            }

            if (index < currentIndex)
            {
                throw new DataPortabilityException(
                    $"Line {lineNumber}: rows of '{table}' appear after rows of '{manifest.Tables[currentIndex]}'; " +
                    "tables must follow the manifest order.");
            }

            currentIndex = index;
            counts[table]++;
        }

        if (end == null)
        {
            throw new DataPortabilityException(
                $"Export file is incomplete: the final '{ExportEndMarker.TableName}' line is missing " +
                "(the file was truncated or the export did not finish). Copy the file again or re-run the export.");
        }

        foreach (var (table, count) in counts)
        {
            var expected = end.RowCounts.TryGetValue(table, out var c) ? c : -1;
            if (expected != count)
            {
                throw new DataPortabilityException(
                    $"Export file is inconsistent: table '{table}' has {count} rows but the end line declares {expected}.");
            }
        }

        return new ExportFileSummary(manifest, counts);
    }

    /// <summary>
    /// Reads the "table" member of a row or end line.
    /// </summary>
    /// <param name="line">The line text.</param>
    /// <param name="lineNumber">1-based line number for messages.</param>
    /// <returns>The table name.</returns>
    /// <exception cref="DataPortabilityException">Thrown when the line is not valid JSON or has no table.</exception>
    public static string ReadTableName(string line, int lineNumber)
    {
        try
        {
            // The writer always emits "table" first; read just that token.
            var reader = new Utf8JsonReader(Encoding.UTF8.GetBytes(line));
            if (reader.Read() && reader.TokenType == JsonTokenType.StartObject
                && reader.Read() && reader.TokenType == JsonTokenType.PropertyName && reader.ValueTextEquals("table")
                && reader.Read() && reader.TokenType == JsonTokenType.String)
            {
                var table = reader.GetString()!;

                // Walk the rest of the line so malformed or cut-off JSON is
                // reported here, before the import transaction starts.
                while (reader.Read())
                {
                }

                return table;
            }
        }
        catch (JsonException ex)
        {
            throw new DataPortabilityException($"Line {lineNumber}: invalid JSON ({ex.Message}).", ex);
        }

        throw new DataPortabilityException($"Line {lineNumber}: expected an object starting with a \"table\" member.");
    }

    private static ExportEndMarker ParseEnd(string line, int lineNumber)
    {
        try
        {
            return JsonSerializer.Deserialize<ExportEndMarker>(line, ExportManifest.JsonOptions)
                ?? throw new DataPortabilityException($"Line {lineNumber}: invalid end line.");
        }
        catch (JsonException ex)
        {
            throw new DataPortabilityException($"Line {lineNumber}: invalid end line ({ex.Message}).", ex);
        }
    }
}
