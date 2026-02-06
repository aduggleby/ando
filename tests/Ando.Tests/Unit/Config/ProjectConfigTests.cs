// =============================================================================
// ProjectConfigTests.cs
//
// Summary: Unit tests for ProjectConfig class.
//
// Tests verify that:
// - Load returns defaults when no config file exists
// - Load correctly parses dind:true and dind:false
// - Load handles invalid JSON gracefully (returns defaults)
// - Save writes valid JSON
// - Round-trip (save then load) preserves values
// =============================================================================

using Ando.Config;

namespace Ando.Tests.Unit.Config;

[Trait("Category", "Unit")]
public class ProjectConfigTests : IDisposable
{
    private readonly string _tempDir;

    public ProjectConfigTests()
    {
        // Create a unique temp directory for each test.
        _tempDir = Path.Combine(Path.GetTempPath(), $"ando-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        // Clean up temp directory after test.
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenNoFileExists()
    {
        // Act
        var config = ProjectConfig.Load(_tempDir);

        // Assert
        config.Dind.ShouldBeFalse();
    }

    [Fact]
    public void Load_ParsesDindTrue()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, ProjectConfig.FileName);
        File.WriteAllText(configPath, """{"dind": true}""");

        // Act
        var config = ProjectConfig.Load(_tempDir);

        // Assert
        config.Dind.ShouldBeTrue();
    }

    [Fact]
    public void Load_ParsesDindFalse()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, ProjectConfig.FileName);
        File.WriteAllText(configPath, """{"dind": false}""");

        // Act
        var config = ProjectConfig.Load(_tempDir);

        // Assert
        config.Dind.ShouldBeFalse();
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenJsonIsInvalid()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, ProjectConfig.FileName);
        File.WriteAllText(configPath, "not valid json {{{");

        // Act
        var config = ProjectConfig.Load(_tempDir);

        // Assert - should return defaults, not throw
        config.Dind.ShouldBeFalse();
    }

    [Fact]
    public void Load_ReturnsDefaults_WhenJsonIsEmpty()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, ProjectConfig.FileName);
        File.WriteAllText(configPath, "{}");

        // Act
        var config = ProjectConfig.Load(_tempDir);

        // Assert
        config.Dind.ShouldBeFalse();
    }

    [Fact]
    public void Save_WritesValidJson()
    {
        // Arrange
        var config = new ProjectConfig { Dind = true };

        // Act
        config.Save(_tempDir);

        // Assert
        var configPath = Path.Combine(_tempDir, ProjectConfig.FileName);
        File.Exists(configPath).ShouldBeTrue();

        var json = File.ReadAllText(configPath);
        json.ShouldContain("\"dind\"");
        json.ShouldContain("true");
    }

    [Fact]
    public void Save_CreatesIndentedJson()
    {
        // Arrange
        var config = new ProjectConfig { Dind = true };

        // Act
        config.Save(_tempDir);

        // Assert - indented JSON has newlines
        var configPath = Path.Combine(_tempDir, ProjectConfig.FileName);
        var json = File.ReadAllText(configPath);
        json.ShouldContain("\n");
    }

    [Fact]
    public void RoundTrip_PreservesValues()
    {
        // Arrange
        var original = new ProjectConfig { Dind = true };

        // Act
        original.Save(_tempDir);
        var loaded = ProjectConfig.Load(_tempDir);

        // Assert
        loaded.Dind.ShouldBe(original.Dind);
    }

    [Fact]
    public void FileName_IsAndoConfig()
    {
        // Assert
        ProjectConfig.FileName.ShouldBe("ando.config");
    }

    [Fact]
    public void Load_ParsesReadEnvTrue()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, ProjectConfig.FileName);
        File.WriteAllText(configPath, """{"readEnv": true}""");

        // Act
        var config = ProjectConfig.Load(_tempDir);

        // Assert
        config.ReadEnv.ShouldBeTrue();
    }

    [Fact]
    public void Load_ParsesReadEnvFalse()
    {
        // Arrange
        var configPath = Path.Combine(_tempDir, ProjectConfig.FileName);
        File.WriteAllText(configPath, """{"readEnv": false}""");

        // Act
        var config = ProjectConfig.Load(_tempDir);

        // Assert
        config.ReadEnv.ShouldBeFalse();
    }

    [Fact]
    public void RoundTrip_PreservesReadEnv()
    {
        // Arrange
        var original = new ProjectConfig { ReadEnv = true };

        // Act
        original.Save(_tempDir);
        var loaded = ProjectConfig.Load(_tempDir);

        // Assert
        loaded.ReadEnv.ShouldBe(original.ReadEnv);
    }

    [Fact]
    public void RoundTrip_PreservesBothSettings()
    {
        // Arrange
        var original = new ProjectConfig { Dind = true, ReadEnv = true };

        // Act
        original.Save(_tempDir);
        var loaded = ProjectConfig.Load(_tempDir);

        // Assert
        loaded.Dind.ShouldBe(original.Dind);
        loaded.ReadEnv.ShouldBe(original.ReadEnv);
    }
}
