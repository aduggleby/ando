// =============================================================================
// ProjectConfigMergeTests.cs
//
// Tests to verify that saving config settings preserves existing values.
// This prevents the bug where saving one setting (e.g., allowClaude: true)
// would overwrite other existing settings (e.g., dind: true, readEnv: true).
// =============================================================================

using Ando.Config;
using Shouldly;

namespace Ando.Tests.Unit.Config;

[Trait("Category", "Unit")]
public class ProjectConfigMergeTests : IDisposable
{
    private readonly string _testDir;

    public ProjectConfigMergeTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"config-merge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDir))
            Directory.Delete(_testDir, recursive: true);
    }

    [Fact]
    public void Save_WithExistingConfig_PreservesOtherSettings()
    {
        // Arrange: Create config with dind and readEnv set.
        var originalConfig = new ProjectConfig { Dind = true, ReadEnv = true };
        originalConfig.Save(_testDir);

        // Act: Load config, modify only allowClaude, save.
        var loaded = ProjectConfig.Load(_testDir);
        var modified = loaded with { AllowClaude = true };
        modified.Save(_testDir);

        // Assert: All settings should be preserved.
        var final = ProjectConfig.Load(_testDir);
        final.Dind.ShouldBeTrue("Dind should be preserved");
        final.ReadEnv.ShouldBeTrue("ReadEnv should be preserved");
        final.AllowClaude.ShouldBeTrue("AllowClaude should be set");
    }

    [Fact]
    public void Save_AddingDind_PreservesAllowClaude()
    {
        // Arrange: Create config with allowClaude set.
        var originalConfig = new ProjectConfig { AllowClaude = true };
        originalConfig.Save(_testDir);

        // Act: Load config, modify only dind, save.
        var loaded = ProjectConfig.Load(_testDir);
        var modified = loaded with { Dind = true };
        modified.Save(_testDir);

        // Assert: Both settings should be present.
        var final = ProjectConfig.Load(_testDir);
        final.AllowClaude.ShouldBeTrue("AllowClaude should be preserved");
        final.Dind.ShouldBeTrue("Dind should be set");
    }

    [Fact]
    public void Save_AddingReadEnv_PreservesOtherSettings()
    {
        // Arrange: Create config with dind and allowClaude set.
        var originalConfig = new ProjectConfig { Dind = true, AllowClaude = true };
        originalConfig.Save(_testDir);

        // Act: Load config, modify only readEnv, save.
        var loaded = ProjectConfig.Load(_testDir);
        var modified = loaded with { ReadEnv = true };
        modified.Save(_testDir);

        // Assert: All settings should be present.
        var final = ProjectConfig.Load(_testDir);
        final.Dind.ShouldBeTrue("Dind should be preserved");
        final.AllowClaude.ShouldBeTrue("AllowClaude should be preserved");
        final.ReadEnv.ShouldBeTrue("ReadEnv should be set");
    }

    [Fact]
    public void Load_NonExistentFile_ReturnsDefaults()
    {
        var config = ProjectConfig.Load(_testDir);

        config.Dind.ShouldBeFalse();
        config.ReadEnv.ShouldBeFalse();
        config.AllowClaude.ShouldBeFalse();
    }

    [Fact]
    public void Load_EmptyFile_ReturnsDefaults()
    {
        File.WriteAllText(Path.Combine(_testDir, "ando.config"), "{}");

        var config = ProjectConfig.Load(_testDir);

        config.Dind.ShouldBeFalse();
        config.ReadEnv.ShouldBeFalse();
        config.AllowClaude.ShouldBeFalse();
    }

    [Fact]
    public void Load_PartialConfig_ReturnsDefaultsForMissing()
    {
        File.WriteAllText(Path.Combine(_testDir, "ando.config"), """{"dind": true}""");

        var config = ProjectConfig.Load(_testDir);

        config.Dind.ShouldBeTrue();
        config.ReadEnv.ShouldBeFalse();
        config.AllowClaude.ShouldBeFalse();
    }

    [Fact]
    public void WithPattern_CreatesNewInstanceWithModifiedValue()
    {
        var original = new ProjectConfig { Dind = true, ReadEnv = true, AllowClaude = false };

        var modified = original with { AllowClaude = true };

        // Original should be unchanged (immutable).
        original.AllowClaude.ShouldBeFalse();
        original.Dind.ShouldBeTrue();
        original.ReadEnv.ShouldBeTrue();

        // Modified should have the change and preserve other values.
        modified.AllowClaude.ShouldBeTrue();
        modified.Dind.ShouldBeTrue();
        modified.ReadEnv.ShouldBeTrue();
    }
}
