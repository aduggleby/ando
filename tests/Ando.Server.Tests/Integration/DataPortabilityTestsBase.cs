// =============================================================================
// DataPortabilityTestsBase.cs
//
// Summary: Data export/import tests on one provider (source and target are two
// databases on the same server): same-provider round trip and every refusal
// path of the import.
//
// The cross-provider round trips (SQL Server -> PostgreSQL and back) live in
// DataPortabilityCrossProviderTests; this base keeps import coverage for a
// single-provider run (ANDO_TEST_DB_PROVIDERS=SqlServer or PostgreSql).
//
// Design Decisions:
// - Each test creates its own migrated databases and temp directory
// - Refusal tests assert both the message and that the target is unchanged
//   (the transaction rolled back or nothing was started)
// =============================================================================

using System.Text.Json;
using System.Text.Json.Nodes;
using Ando.Server.DataPortability;
using Ando.Server.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;

namespace Ando.Server.Tests.Integration;

/// <summary>
/// Data portability tests shared by all database providers.
/// </summary>
[Trait("Category", "Integration")]
public abstract class DataPortabilityTestsBase : IAsyncLifetime
{
    private readonly IDatabaseFixture _fixture;
    private readonly DirectoryInfo _directory;
    private PortabilityTestDatabase? _source;
    private PortabilityTestDatabase? _target;
    private string _exportPath = "";

    /// <summary>
    /// Binds the tests to a provider's container, or skips them when it is not selected.
    /// </summary>
    /// <param name="fixture">Container fixture.</param>
    protected DataPortabilityTestsBase(IDatabaseFixture fixture)
    {
        Skip.IfNot(DatabaseTestProviders.IsSelected(fixture.Provider), DatabaseTestProviders.SkipReason(fixture.Provider));
        _fixture = fixture;
        _directory = Directory.CreateTempSubdirectory("ando-portability-");
    }

    /// <summary>
    /// Creates a seeded source, a migrated target with the startup roles, and an export file.
    /// </summary>
    public async Task InitializeAsync()
    {
        _source = await PortabilityTestDatabase.CreateMigratedAsync(_fixture);
        _target = await PortabilityTestDatabase.CreateMigratedAsync(_fixture);
        await DataPortabilityTestData.SeedAsync(_source);
        await DataPortabilityTestData.SeedStartupRolesAsync(_target);
        _exportPath = Path.Combine(_directory.FullName, "export.jsonl");
        await DataPortabilityTestData.ExportAsync(_source, _exportPath);
    }

    /// <summary>
    /// Drops both databases and the temp directory.
    /// </summary>
    public async Task DisposeAsync()
    {
        if (_source != null) await _source.DisposeAsync();
        if (_target != null) await _target.DisposeAsync();
        _directory.Delete(recursive: true);
    }

    [SkippableFact]
    public async Task RoundTrip_SameProvider_PreservesAllData()
    {
        // Uses its own databases so the assertions do not depend on InitializeAsync's export.
        await DataPortabilityTestData.AssertRoundTripAsync(_fixture, _fixture);
    }

    [SkippableFact]
    public async Task Export_ExistingFile_RequiresOverwrite()
    {
        await using var db = _source!.CreateContext();
        var service = new DataExportService(db);

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => service.ExportAsync(_exportPath, overwrite: false));
        ex.Message.ShouldContain("--overwrite");

        var result = await service.ExportAsync(_exportPath, overwrite: true);
        result.RowCounts.Sum(c => c.Value).ShouldBeGreaterThan(DataPortabilityTestData.LogEntryCount);
        File.Exists(_exportPath + ".partial").ShouldBeFalse();
    }

    [SkippableFact]
    public async Task Import_NonEmptyTarget_IsRefusedWithoutForce_AndReplacedWithForce()
    {
        await DataPortabilityTestData.ImportAsync(_target!, _exportPath);
        var first = await DataPortabilityTestData.SnapshotAsync(_target!);

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => DataPortabilityTestData.ImportAsync(_target!, _exportPath));
        ex.Message.ShouldContain("Target database is not empty");
        ex.Message.ShouldContain("--force");

        var forced = await DataPortabilityTestData.ImportAsync(_target!, _exportPath, force: true);
        forced.DeletedRows.ShouldBe(first.Counts.Values.Sum());

        var second = await DataPortabilityTestData.SnapshotAsync(_target!);
        second.Checksums.ShouldBe(first.Checksums);
    }

    [SkippableFact]
    public async Task Import_ModelFingerprintMismatch_IsRefused()
    {
        var lines = await File.ReadAllLinesAsync(_exportPath);
        var manifest = JsonNode.Parse(lines[0])!.AsObject();
        manifest["modelFingerprint"] = "0000";
        manifest["lastMigration"] = "20990101000000_FutureMigration";
        manifest["lastMigrationName"] = "FutureMigration";
        lines[0] = manifest.ToJsonString();
        await File.WriteAllLinesAsync(_exportPath, lines);

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => DataPortabilityTestData.ImportAsync(_target!, _exportPath));

        ex.Message.ShouldContain("does not match this Ando.Server version");
        ex.Message.ShouldContain("'FutureMigration'");
        await using var db = _target!.CreateContext();
        var targetLast = (await db.Database.GetAppliedMigrationsAsync()).Last();
        ex.Message.ShouldContain($"'{ExportManifest.MigrationName(targetLast)}'");
        await AssertTargetUntouchedAsync();
    }

    [SkippableFact]
    public async Task Import_TruncatedFile_IsRefused()
    {
        var lines = await File.ReadAllLinesAsync(_exportPath);
        await File.WriteAllLinesAsync(_exportPath, lines[..^1]);

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => DataPortabilityTestData.ImportAsync(_target!, _exportPath));

        ex.Message.ShouldContain("incomplete");
        await AssertTargetUntouchedAsync();
    }

    [SkippableFact]
    public async Task Import_WrongEncryptionKey_RollsBackEverything()
    {
        var ex = await Should.ThrowAsync<DataPortabilityException>(
            () => DataPortabilityTestData.ImportAsync(_target!, _exportPath, key: DataPortabilityTestData.OtherKey));

        ex.Message.ShouldContain("Encryption__Key differs from the source's");
        await AssertTargetUntouchedAsync();
    }

    [SkippableFact]
    public async Task Import_InvalidRowValue_RollsBackEverything()
    {
        // Corrupt one log entry near the end, after thousands of rows were inserted.
        var lines = await File.ReadAllLinesAsync(_exportPath);
        var index = Array.FindLastIndex(lines, l => l.StartsWith("{\"table\":\"BuildLogEntries\"", StringComparison.Ordinal));
        var row = JsonNode.Parse(lines[index])!.AsObject();
        row["row"]!["Type"] = "NotAType";
        lines[index] = row.ToJsonString(new JsonSerializerOptions { Encoder = DataExportService.WriterOptions.Encoder });
        await File.WriteAllLinesAsync(_exportPath, lines);

        var ex = await Should.ThrowAsync<DataPortabilityException>(() => DataPortabilityTestData.ImportAsync(_target!, _exportPath));

        ex.Message.ShouldContain($"Line {index + 1}");
        ex.Message.ShouldContain("BuildLogEntries.Type");
        await AssertTargetUntouchedAsync();
    }

    private async Task AssertTargetUntouchedAsync()
    {
        await using var db = _target!.CreateContext();
        (await db.Users.CountAsync()).ShouldBe(0);
        (await db.BuildLogEntries.CountAsync()).ShouldBe(0);
        (await db.Roles.Select(r => r.Name).OrderBy(n => n).ToListAsync()).ShouldBe(["Admin", "User"]);
    }
}
