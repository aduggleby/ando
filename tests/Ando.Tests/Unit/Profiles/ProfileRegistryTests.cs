// =============================================================================
// ProfileRegistryTests.cs
//
// Summary: Unit tests for ProfileRegistry class.
//
// Tests verify profile definition, activation, validation, and case-insensitivity.
// =============================================================================

using Ando.Profiles;

namespace Ando.Tests.Unit.Profiles;

[Trait("Category", "Unit")]
public class ProfileRegistryTests
{
    [Fact]
    public void Define_AddsProfileToDefinedProfiles()
    {
        var registry = new ProfileRegistry();

        registry.Define("push");

        Assert.Contains("push", registry.DefinedProfiles);
    }

    [Fact]
    public void Define_ReturnsProfileObject()
    {
        var registry = new ProfileRegistry();

        var profile = registry.Define("release");

        Assert.NotNull(profile);
    }

    [Fact]
    public void Define_WithEmptyName_ThrowsArgumentException()
    {
        var registry = new ProfileRegistry();

        Assert.Throws<ArgumentException>(() => registry.Define(""));
    }

    [Fact]
    public void Define_WithWhitespaceName_ThrowsArgumentException()
    {
        var registry = new ProfileRegistry();

        Assert.Throws<ArgumentException>(() => registry.Define("   "));
    }

    [Fact]
    public void SetActiveProfiles_SetsActiveProfiles()
    {
        var registry = new ProfileRegistry();

        registry.SetActiveProfiles(["push", "release"]);

        Assert.Contains("push", registry.ActiveProfiles);
        Assert.Contains("release", registry.ActiveProfiles);
    }

    [Fact]
    public void SetActiveProfiles_ClearsPreviousProfiles()
    {
        var registry = new ProfileRegistry();
        registry.SetActiveProfiles(["old"]);

        registry.SetActiveProfiles(["new"]);

        Assert.DoesNotContain("old", registry.ActiveProfiles);
        Assert.Contains("new", registry.ActiveProfiles);
    }

    [Fact]
    public void IsActive_ReturnsTrueForActiveProfile()
    {
        var registry = new ProfileRegistry();
        registry.SetActiveProfiles(["push"]);

        Assert.True(registry.IsActive("push"));
    }

    [Fact]
    public void IsActive_ReturnsFalseForInactiveProfile()
    {
        var registry = new ProfileRegistry();
        registry.SetActiveProfiles(["push"]);

        Assert.False(registry.IsActive("release"));
    }

    [Fact]
    public void IsActive_IsCaseInsensitive()
    {
        var registry = new ProfileRegistry();
        registry.SetActiveProfiles(["Push"]);

        Assert.True(registry.IsActive("push"));
        Assert.True(registry.IsActive("PUSH"));
        Assert.True(registry.IsActive("Push"));
    }

    [Fact]
    public void Validate_WithValidProfiles_DoesNotThrow()
    {
        var registry = new ProfileRegistry();
        registry.Define("push");
        registry.Define("release");
        registry.SetActiveProfiles(["push", "release"]);

        var exception = Record.Exception(() => registry.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void Validate_WithUnknownProfile_ThrowsInvalidOperationException()
    {
        var registry = new ProfileRegistry();
        registry.Define("push");
        registry.SetActiveProfiles(["unknown"]);

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Validate());
        Assert.Contains("unknown", exception.Message);
        Assert.Contains("'unknown'", exception.Message);
    }

    [Fact]
    public void Validate_WithMultipleUnknownProfiles_ListsAllUnknown()
    {
        var registry = new ProfileRegistry();
        registry.Define("push");
        registry.SetActiveProfiles(["bad1", "bad2"]);

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Validate());
        Assert.Contains("bad1", exception.Message);
        Assert.Contains("bad2", exception.Message);
    }

    [Fact]
    public void Validate_ListsAvailableProfiles()
    {
        var registry = new ProfileRegistry();
        registry.Define("push");
        registry.Define("release");
        registry.SetActiveProfiles(["unknown"]);

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Validate());
        Assert.Contains("push", exception.Message);
        Assert.Contains("release", exception.Message);
    }

    [Fact]
    public void Validate_WhenNoProfilesDefined_ShowsNoneDefined()
    {
        var registry = new ProfileRegistry();
        registry.SetActiveProfiles(["unknown"]);

        var exception = Assert.Throws<InvalidOperationException>(() => registry.Validate());
        Assert.Contains("(none defined)", exception.Message);
    }

    [Fact]
    public void Validate_IsCaseInsensitive()
    {
        var registry = new ProfileRegistry();
        registry.Define("Push");
        registry.SetActiveProfiles(["push"]);

        var exception = Record.Exception(() => registry.Validate());

        Assert.Null(exception);
    }

    [Fact]
    public void DefinedProfiles_ReturnsReadOnlyCollection()
    {
        var registry = new ProfileRegistry();
        registry.Define("push");

        var profiles = registry.DefinedProfiles;

        Assert.IsAssignableFrom<IReadOnlyCollection<string>>(profiles);
    }

    [Fact]
    public void ActiveProfiles_ReturnsReadOnlyCollection()
    {
        var registry = new ProfileRegistry();
        registry.SetActiveProfiles(["push"]);

        var profiles = registry.ActiveProfiles;

        Assert.IsAssignableFrom<IReadOnlyCollection<string>>(profiles);
    }
}
