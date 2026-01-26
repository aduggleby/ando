// =============================================================================
// DindCheckerTests.cs
//
// Summary: Unit tests for DindChecker class.
//
// Tests verify that:
// - Returns NotRequired when no DIND operations exist
// - Returns EnabledViaFlag when --dind flag is provided
// - Returns EnabledViaConfig when ando.config has dind:true
// - Correctly detects Docker.Build, Docker.Push, GitHub.PushImage operations
// - ShouldEnableDind returns correct values for each result type
// - GetDindOperations finds DIND-requiring operations
//
// Note: Interactive prompt tests are not included as they require Console input.
// =============================================================================

using Ando.Config;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Utilities;

namespace Ando.Tests.Unit.Utilities;

[Trait("Category", "Unit")]
public class DindCheckerTests : IDisposable
{
    private readonly TestLogger _logger = new();
    private readonly string _tempDir;

    public DindCheckerTests()
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
    public void CheckAndPrompt_ReturnsNotRequired_WhenNoSteps()
    {
        // Arrange
        var registry = new StepRegistry();
        var checker = new DindChecker(_logger);

        // Act
        var result = checker.CheckAndPrompt(registry, hasDindFlag: false, _tempDir);

        // Assert
        result.ShouldBe(DindCheckResult.NotRequired);
    }

    [Fact]
    public void CheckAndPrompt_ReturnsNotRequired_WhenNoDindOperations()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Dotnet.Build", () => Task.FromResult(true));
        registry.Register("Dotnet.Test", () => Task.FromResult(true));
        var checker = new DindChecker(_logger);

        // Act
        var result = checker.CheckAndPrompt(registry, hasDindFlag: false, _tempDir);

        // Assert
        result.ShouldBe(DindCheckResult.NotRequired);
    }

    [Fact]
    public void CheckAndPrompt_ReturnsEnabledViaFlag_WhenDindFlagProvided()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Docker.Build", () => Task.FromResult(true));
        var checker = new DindChecker(_logger);

        // Act
        var result = checker.CheckAndPrompt(registry, hasDindFlag: true, _tempDir);

        // Assert
        result.ShouldBe(DindCheckResult.EnabledViaFlag);
    }

    [Fact]
    public void CheckAndPrompt_ReturnsEnabledViaConfig_WhenConfigHasDindTrue()
    {
        // Arrange - create ando.config with dind:true
        var config = new ProjectConfig { Dind = true };
        config.Save(_tempDir);

        var registry = new StepRegistry();
        registry.Register("Docker.Build", () => Task.FromResult(true));
        var checker = new DindChecker(_logger);

        // Act
        var result = checker.CheckAndPrompt(registry, hasDindFlag: false, _tempDir);

        // Assert
        result.ShouldBe(DindCheckResult.EnabledViaConfig);
    }

    [Fact]
    public void GetDindOperations_DetectsDockerBuild()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Docker.Build", () => Task.FromResult(true));

        // Act
        var ops = DindChecker.GetDindOperations(registry);

        // Assert
        ops.ShouldContain("Docker.Build");
    }

    [Fact]
    public void GetDindOperations_DetectsDockerPush()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Docker.Push", () => Task.FromResult(true));

        // Act
        var ops = DindChecker.GetDindOperations(registry);

        // Assert
        ops.ShouldContain("Docker.Push");
    }

    [Fact]
    public void GetDindOperations_DetectsGitHubPushImage()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("GitHub.PushImage", () => Task.FromResult(true));

        // Act
        var ops = DindChecker.GetDindOperations(registry);

        // Assert
        ops.ShouldContain("GitHub.PushImage");
    }

    [Fact]
    public void GetDindOperations_DetectsMultipleOperations()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Docker.Build", () => Task.FromResult(true));
        registry.Register("Docker.Push", () => Task.FromResult(true));
        registry.Register("Dotnet.Build", () => Task.FromResult(true));

        // Act
        var ops = DindChecker.GetDindOperations(registry);

        // Assert
        ops.Count.ShouldBe(2);
        ops.ShouldContain("Docker.Build");
        ops.ShouldContain("Docker.Push");
    }

    [Fact]
    public void GetDindOperations_IsCaseInsensitive()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("docker.build", () => Task.FromResult(true));

        // Act
        var ops = DindChecker.GetDindOperations(registry);

        // Assert
        ops.Count.ShouldBe(1);
    }

    [Fact]
    public void GetDindOperations_ReturnsEmpty_WhenNoDindOperations()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Dotnet.Build", () => Task.FromResult(true));
        registry.Register("Npm.Install", () => Task.FromResult(true));

        // Act
        var ops = DindChecker.GetDindOperations(registry);

        // Assert
        ops.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(DindCheckResult.EnabledViaFlag, true)]
    [InlineData(DindCheckResult.EnabledViaConfig, true)]
    [InlineData(DindCheckResult.EnabledThisRun, true)]
    [InlineData(DindCheckResult.EnabledAndSaved, true)]
    [InlineData(DindCheckResult.NotRequired, false)]
    [InlineData(DindCheckResult.Cancelled, false)]
    public void ShouldEnableDind_ReturnsCorrectValue(DindCheckResult input, bool expected)
    {
        // Act
        var result = DindChecker.ShouldEnableDind(input);

        // Assert
        result.ShouldBe(expected);
    }

    [Fact]
    public void CheckAndPrompt_LogsDebugMessage_WhenEnabledViaFlag()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Docker.Build", () => Task.FromResult(true));
        var checker = new DindChecker(_logger);

        // Act
        checker.CheckAndPrompt(registry, hasDindFlag: true, _tempDir);

        // Assert
        _logger.DebugMessages.ShouldContain(m => m.Contains("DIND enabled via --dind flag"));
    }

    [Fact]
    public void CheckAndPrompt_LogsDebugMessage_WhenEnabledViaConfig()
    {
        // Arrange
        var config = new ProjectConfig { Dind = true };
        config.Save(_tempDir);

        var registry = new StepRegistry();
        registry.Register("Docker.Build", () => Task.FromResult(true));
        var checker = new DindChecker(_logger);

        // Act
        checker.CheckAndPrompt(registry, hasDindFlag: false, _tempDir);

        // Assert
        _logger.DebugMessages.ShouldContain(m => m.Contains("DIND enabled via ando.config"));
    }

    [Fact]
    public void CheckAndPrompt_FlagTakesPrecedenceOverConfig()
    {
        // Arrange - config has dind:false, but flag is provided
        var config = new ProjectConfig { Dind = false };
        config.Save(_tempDir);

        var registry = new StepRegistry();
        registry.Register("Docker.Build", () => Task.FromResult(true));
        var checker = new DindChecker(_logger);

        // Act
        var result = checker.CheckAndPrompt(registry, hasDindFlag: true, _tempDir);

        // Assert - flag should take precedence
        result.ShouldBe(DindCheckResult.EnabledViaFlag);
    }
}
