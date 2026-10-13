// =============================================================================
// ExportFileReaderTests.cs
//
// Summary: Unit tests for the structural checks run on an export file before
// an import touches the database (manifest, table order, end marker, counts).
//
// Design Decisions:
// - Files are written to a per-test temp directory with hand-built lines, so
//   every malformed variant is explicit
// =============================================================================

using System.Text.Json;
using Ando.Server.DataPortability;

namespace Ando.Server.Tests.Unit.DataPortability;

/// <summary>
/// Tests for <see cref="ExportFileReader"/>.
/// </summary>
public sealed class ExportFileReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"ando-export-reader-{Guid.NewGuid():N}");

    /// <summary>
    /// Creates the temp directory.
    /// </summary>
    public ExportFileReaderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    /// <summary>
    /// Removes the temp directory.
    /// </summary>
    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static string ManifestLine(Func<ExportManifest, ExportManifest>? edit = null)
    {
        var manifest = new ExportManifest
        {
            Format = ExportManifest.FormatName,
            FormatVersion = ExportManifest.CurrentFormatVersion,
            AppVersion = "test",
            ExportedAtUtc = new DateTime(2026, 10, 13, 0, 0, 0, DateTimeKind.Utc),
            SourceProvider = "SqlServer",
            LastMigration = "20261013075421_AddProjectSecretNormalizedName",
            LastMigrationName = "AddProjectSecretNormalizedName",
            ModelFingerprint = "abc",
            Tables = PortableTables.TableNames.ToList()
        };
        return JsonSerializer.Serialize(edit?.Invoke(manifest) ?? manifest, ExportManifest.JsonOptions);
    }

    private static string Row(string table, string row = "{\"Id\":1}") => $"{{\"table\":\"{table}\",\"row\":{row}}}";

    private static string End(Dictionary<string, long> counts)
    {
        var all = PortableTables.TableNames.ToDictionary(t => t, t => counts.GetValueOrDefault(t));
        return JsonSerializer.Serialize(new ExportEndMarker { RowCounts = all }, ExportManifest.JsonOptions);
    }

    private string WriteFile(params string[] lines)
    {
        var path = Path.Combine(_directory, $"{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(path, string.Join("\n", lines) + "\n");
        return path;
    }

    [Fact]
    public async Task ScanAsync_ValidFile_ReturnsManifestAndCounts()
    {
        var path = WriteFile(
            ManifestLine(),
            Row("AspNetUsers"),
            Row("Projects"),
            Row("Projects", "{\"Id\":2}"),
            End(new() { ["AspNetUsers"] = 1, ["Projects"] = 2 }));

        var summary = await ExportFileReader.ScanAsync(path, CancellationToken.None);

        summary.Manifest.LastMigrationName.ShouldBe("AddProjectSecretNormalizedName");
        summary.Manifest.ExportedAtUtc.Kind.ShouldBe(DateTimeKind.Utc);
        summary.RowCounts["Projects"].ShouldBe(2);
        summary.RowCounts["AspNetUsers"].ShouldBe(1);
        summary.RowCounts["Builds"].ShouldBe(0);
    }

    [Fact]
    public async Task ScanAsync_MissingEndLine_IsTruncated()
    {
        var path = WriteFile(ManifestLine(), Row("Projects"));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("incomplete");
        ex.Message.ShouldContain("__end");
    }

    [Fact]
    public async Task ScanAsync_CountMismatch_Fails()
    {
        var path = WriteFile(ManifestLine(), Row("Projects"), End(new() { ["Projects"] = 2 }));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("'Projects' has 1 rows but the end line declares 2");
    }

    [Fact]
    public async Task ScanAsync_TablesOutOfOrder_Fails()
    {
        var path = WriteFile(ManifestLine(), Row("Projects"), Row("AspNetUsers"), End(new() { ["Projects"] = 1, ["AspNetUsers"] = 1 }));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("Line 3");
        ex.Message.ShouldContain("manifest order");
    }

    [Fact]
    public async Task ScanAsync_UnknownTable_Fails()
    {
        var path = WriteFile(ManifestLine(), Row("HangfireJob"), End(new()));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("unknown table 'HangfireJob'");
    }

    [Fact]
    public async Task ScanAsync_DataAfterEnd_Fails()
    {
        var path = WriteFile(ManifestLine(), End(new()), Row("Projects"));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("after the '__end' line");
    }

    [Fact]
    public async Task ScanAsync_NotAManifest_Fails()
    {
        var path = WriteFile("{\"hello\":1}", End(new()));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("Not an Ando export file");
    }

    [Fact]
    public async Task ScanAsync_NewerFormatVersion_Fails()
    {
        var path = WriteFile(ManifestLine(m => m with { FormatVersion = 2 }), End(new()));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("Unsupported export format version 2");
    }

    [Fact]
    public async Task ScanAsync_DifferentTableList_Fails()
    {
        var path = WriteFile(ManifestLine(m => m with { Tables = ["Projects"] }), End(new()));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("Use the same Ando.Server version");
    }

    [Fact]
    public async Task ScanAsync_InvalidJsonRow_ReportsLineNumber()
    {
        var path = WriteFile(ManifestLine(), "{\"table\":\"Projects\",\"row\":", End(new()));

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => ExportFileReader.ScanAsync(path, CancellationToken.None));
        ex.Message.ShouldContain("Line 2");
    }

    [Fact]
    public async Task ScanAsync_MissingFile_Fails()
    {
        var ex = await Should.ThrowAsync<DataPortabilityException>(
            () => ExportFileReader.ScanAsync(Path.Combine(_directory, "nope.jsonl"), CancellationToken.None));
        ex.Message.ShouldContain("does not exist");
    }
}
