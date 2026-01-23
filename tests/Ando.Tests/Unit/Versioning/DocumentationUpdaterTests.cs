// =============================================================================
// DocumentationUpdaterTests.cs
//
// Unit tests for DocumentationUpdater functionality.
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

    #region Changelog Tests

    [Fact]
    public void UpdateDocumentation_Changelog_AddsEntry()
    {
        var changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        File.WriteAllText(changelogPath, "# Changelog\n\n## 1.0.0\n\n- Initial release\n");

        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1");

        var result = results.Single(r => r.FilePath == "CHANGELOG.md");
        result.Success.ShouldBeTrue();

        var content = File.ReadAllText(changelogPath);
        content.ShouldContain("## 1.0.1");
        content.ShouldContain("- Version bump");
    }

    [Fact]
    public void UpdateDocumentation_Changelog_WithFrontmatter()
    {
        var changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        File.WriteAllText(changelogPath, @"---
title: Changelog
---

## 1.0.0

- Initial release
");

        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1");

        var content = File.ReadAllText(changelogPath);
        // New entry should be after frontmatter.
        var frontmatterEnd = content.IndexOf("---", 3);
        var newEntryPos = content.IndexOf("## 1.0.1");
        newEntryPos.ShouldBeGreaterThan(frontmatterEnd);
    }

    [Fact]
    public void UpdateDocumentation_Changelog_IncludesDate()
    {
        var changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        File.WriteAllText(changelogPath, "# Changelog\n");

        _updater.UpdateDocumentation("1.0.0", "1.0.1");

        var content = File.ReadAllText(changelogPath);
        var today = DateTime.Now.ToString("yyyy-MM-dd");
        content.ShouldContain($"**{today}**");
    }

    [Fact]
    public void UpdateDocumentation_NoChangelog_SkipsQuietly()
    {
        // Don't create a changelog.
        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1");

        results.ShouldBeEmpty();
    }

    [Fact]
    public void UpdateDocumentation_Changelog_WithCommitMessages()
    {
        var changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        File.WriteAllText(changelogPath, "# Changelog\n\n## 1.0.0\n\n- Initial release\n");

        var commitMessages = new List<string>
        {
            "feat: add new feature",
            "fix: bug fix",
            "chore: update dependencies"
        };

        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1", commitMessages);

        var content = File.ReadAllText(changelogPath);
        content.ShouldContain("## 1.0.1");
        content.ShouldContain("- feat: add new feature");
        content.ShouldContain("- fix: bug fix");
        content.ShouldContain("- chore: update dependencies");
        content.ShouldNotContain("- Version bump");
    }

    [Fact]
    public void UpdateDocumentation_Changelog_FiltersVersionBumpCommits()
    {
        var changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        File.WriteAllText(changelogPath, "# Changelog\n");

        var commitMessages = new List<string>
        {
            "feat: actual change",
            "Bump version to 1.0.0",
            "1.0.0"
        };

        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1", commitMessages);

        var content = File.ReadAllText(changelogPath);
        content.ShouldContain("- feat: actual change");
        content.ShouldNotContain("Bump version");
    }

    [Fact]
    public void UpdateDocumentation_Changelog_EmptyCommitMessages_UsesVersionBump()
    {
        var changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        File.WriteAllText(changelogPath, "# Changelog\n");

        var commitMessages = new List<string>();

        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1", commitMessages);

        var content = File.ReadAllText(changelogPath);
        content.ShouldContain("- Version bump");
    }

    [Fact]
    public void UpdateDocumentation_Changelog_OnlyVersionBumpCommits_UsesVersionBump()
    {
        var changelogPath = Path.Combine(_testDir, "CHANGELOG.md");
        File.WriteAllText(changelogPath, "# Changelog\n");

        var commitMessages = new List<string>
        {
            "Bump version to 1.0.0",
            "v1.0.0"
        };

        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1", commitMessages);

        var content = File.ReadAllText(changelogPath);
        content.ShouldContain("- Version bump");
    }

    #endregion

    #region Version Badge Tests

    [Fact]
    public void UpdateDocumentation_VersionBadge_UpdatesQuotedVersion()
    {
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, @"# My Project

Version: ""1.0.0""

Some text.
");

        _updater.UpdateDocumentation("1.0.0", "1.0.1");

        var content = File.ReadAllText(readmePath);
        content.ShouldContain(@"""1.0.1""");
        content.ShouldNotContain(@"""1.0.0""");
    }

    [Fact]
    public void UpdateDocumentation_VersionBadge_UpdatesVPrefix()
    {
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, "Current version: v1.0.0");

        _updater.UpdateDocumentation("1.0.0", "1.0.1");

        var content = File.ReadAllText(readmePath);
        content.ShouldContain("v1.0.1");
        content.ShouldNotContain("v1.0.0");
    }

    [Fact]
    public void UpdateDocumentation_VersionBadge_OnlyUpdatesMatchingVersion()
    {
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, @"# My Project

Current: v1.0.0
Previous: v0.9.0
");

        _updater.UpdateDocumentation("1.0.0", "1.0.1");

        var content = File.ReadAllText(readmePath);
        content.ShouldContain("v1.0.1");
        content.ShouldContain("v0.9.0"); // Old version should be preserved.
    }

    [Fact]
    public void UpdateDocumentation_VersionBadge_NoMatch_ReportsFailure()
    {
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, "# Project\n\nNo version here.");

        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1");

        var result = results.Single(r => r.FilePath == "README.md");
        result.Success.ShouldBeFalse();
        result.Error.ShouldContain("No version pattern found");
    }

    [Fact]
    public void UpdateDocumentation_WebsiteIndexPriority()
    {
        // Create both README and website index.
        var readmePath = Path.Combine(_testDir, "README.md");
        File.WriteAllText(readmePath, "Version: v1.0.0");

        var websitePath = Path.Combine(_testDir, "website", "src", "pages");
        Directory.CreateDirectory(websitePath);
        var indexPath = Path.Combine(websitePath, "index.astro");
        File.WriteAllText(indexPath, "Version: v1.0.0");

        var results = _updater.UpdateDocumentation("1.0.0", "1.0.1");

        // Should update website index (first in search order).
        var result = results.Single(r => r.FilePath.Contains("index.astro"));
        result.Success.ShouldBeTrue();
    }

    #endregion
}
