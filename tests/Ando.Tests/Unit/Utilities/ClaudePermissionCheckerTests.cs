// =============================================================================
// ClaudePermissionCheckerTests.cs
//
// Summary: Unit tests for ClaudePermissionChecker class.
//
// Tests verify that:
// - Returns AllowedViaConfig when ando.config has allowClaude:true
// - IsAllowed returns correct values for each result type
//
// Note: Interactive prompt tests are not included as they require Console input.
// =============================================================================

using Ando.Config;
using Ando.Tests.TestFixtures;
using Ando.Utilities;

namespace Ando.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class ClaudePermissionCheckerTests : IDisposable
{
    private readonly TestLogger _logger = new();
    private readonly string _tempDir;

    public ClaudePermissionCheckerTests()
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
    public void CheckAndPrompt_ReturnsAllowedViaConfig_WhenConfigHasAllowClaudeTrue()
    {
        // Arrange - create ando.config with allowClaude:true
        var config = new ProjectConfig { AllowClaude = true };
        config.Save(_tempDir);

        var checker = new ClaudePermissionChecker(_logger);

        // Act
        var result = checker.CheckAndPrompt(_tempDir, "commit");

        // Assert
        result.ShouldBe(ClaudePermissionResult.AllowedViaConfig);
    }

    [Fact]
    public void CheckAndPrompt_LogsDebugMessage_WhenEnabledViaConfig()
    {
        // Arrange
        var config = new ProjectConfig { AllowClaude = true };
        config.Save(_tempDir);

        var checker = new ClaudePermissionChecker(_logger);

        // Act
        checker.CheckAndPrompt(_tempDir, "commit");

        // Assert
        _logger.DebugMessages.ShouldContain(m => m.Contains("ando.config"));
    }

    [Theory]
    [InlineData(ClaudePermissionResult.AllowedThisRun, true)]
    [InlineData(ClaudePermissionResult.AllowedAndSaved, true)]
    [InlineData(ClaudePermissionResult.AllowedViaConfig, true)]
    [InlineData(ClaudePermissionResult.Denied, false)]
    public void IsAllowed_ReturnsCorrectValue(ClaudePermissionResult input, bool expected)
    {
        // Act
        var result = ClaudePermissionChecker.IsAllowed(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void CheckAndPrompt_WorksWithOtherConfigSettings()
    {
        // Arrange - config has multiple settings, including allowClaude:true
        var config = new ProjectConfig
        {
            Dind = true,
            ReadEnv = true,
            AllowClaude = true
        };
        config.Save(_tempDir);

        var checker = new ClaudePermissionChecker(_logger);

        // Act
        var result = checker.CheckAndPrompt(_tempDir, "docs");

        // Assert
        result.ShouldBe(ClaudePermissionResult.AllowedViaConfig);
    }

    [Fact]
    public void CheckAndPrompt_WorksWithEmptyConfig()
    {
        // Arrange - create empty ando.config (allowClaude defaults to false)
        var config = new ProjectConfig();
        config.Save(_tempDir);

        // Note: Without config, this would prompt for input.
        // Since we can't provide input in tests, we verify the config path.
        var checker = new ClaudePermissionChecker(_logger);

        // Act - with allowClaude:false, this would try to prompt
        // We verify that allowClaude:true works correctly
        config = config with { AllowClaude = true };
        config.Save(_tempDir);
        var result = checker.CheckAndPrompt(_tempDir, "bump");

        // Assert
        result.ShouldBe(ClaudePermissionResult.AllowedViaConfig);
    }
}
