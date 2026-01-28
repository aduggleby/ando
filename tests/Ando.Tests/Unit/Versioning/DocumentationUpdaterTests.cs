// =============================================================================
// DocumentationUpdaterTests.cs
//
// Unit tests for DocumentationUpdater functionality.
// Note: Changelog updates are now handled by Claude, so only version badge
// tests remain in this file.
// =============================================================================

using Ando.Versioning;
using Shouldly;

namespace Ando.Tests.Unit.Versioning;

[Trait("Category", "Unit")]
public class DocumentationUpdaterTests : IDisposable
{
    private readonly string _testDir;
    private readonly DocumentationUpdater _updater;

    public DocumentationUpdaterTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"doc-updater-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
        _updater = new DocumentationUpdater(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    #region Version Badge Tests

    [Fact]
    public void UpdateVersionBadges_UpdatesQuotedVersion()
    {
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, @"# My Project

Version: ""1.0.0""

Some text.
");

        _updater.UpdateVersionBadges("1.0.0", "1.0.1");

        var content = File.ReadAllText(readmePath);
        content.ShouldContain(@"""1.0.1""");
        content.ShouldNotContain(@"""1.0.0""");
    }

    [Fact]
    public void UpdateVersionBadges_UpdatesVPrefix()
    {
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, "Current version: v1.0.0");

        _updater.UpdateVersionBadges("1.0.0", "1.0.1");

        var content = File.ReadAllText(readmePath);
        content.ShouldContain("v1.0.1");
        content.ShouldNotContain("v1.0.0");
    }

    [Fact]
    public void UpdateVersionBadges_OnlyUpdatesMatchingVersion()
    {
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, @"# My Project

Current: v1.0.0
Previous: v0.9.0
");

        _updater.UpdateVersionBadges("1.0.0", "1.0.1");

        var content = File.ReadAllText(readmePath);
        content.ShouldContain("v1.0.1");
        content.ShouldContain("v0.9.0"); // Old version should be preserved.
    }

    [Fact]
    public void UpdateVersionBadges_NoMatch_ReportsFailure()
    {
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, "# Project\n\nNo version here.");

        var results = _updater.UpdateVersionBadges("1.0.0", "1.0.1");

        var result = results.Single(r => r.FilePath == "README.md");
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("No version pattern found");
    }

    [Fact]
    public void UpdateVersionBadges_WebsiteIndexPriority()
    {
        // Create both README and website index.
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, "Version: v1.0.0");

        var websitePath = Path.Combine(_testDir, "website", "src", "pages");
        Directory.CreateDirectory(websitePath);
        var indexPath = Path.Combine(websitePath, "index.astro");
        File.WriteAllText(indexPath, "Version: v1.0.0");

        var results = _updater.UpdateVersionBadges("1.0.0", "1.0.1");

        // Should update website index (first in search order).
        var result = results.Single(r => r.FilePath.Contains("index.astro"));
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public void UpdateVersionBadges_NoFiles_ReturnsEmpty()
    {
        // Don't create any files.
        var results = _updater.UpdateVersionBadges("1.0.0", "1.0.1");

        results.ShouldBeEmpty();
    }

    #endregion
}
