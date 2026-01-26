// =============================================================================
// DindCheckerTests.cs
//
// Summary: Unit tests for DindChecker class.
//
// Tests verify that:
// - Returns NotRequired when no DIND operations exist
// - Returns EnabledViaFlag when --dind flag is provided
// - Returns EnabledViaConfig when ando.config has dind:true
// - Correctly detects Docker.Build, Docker.Push, Docker.Install, GitHub.PushImage,
//   Playwright.Test operations
// - ShouldEnableDind returns correct values for each result type
// - GetDindOperations finds DIND-requiring operations
// - Child builds are scanned for DIND operations
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

    [Fact]
    public void GetDindOperations_DetectsDockerInstall()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Docker.Install", () => Task.FromResult(true));

        // Act
        var ops = DindChecker.GetDindOperations(registry);

        // Assert
        ops.ShouldContain("Docker.Install");
    }

    [Fact]
    public void GetDindOperations_DetectsPlaywrightTest()
    {
        // Arrange
        var registry = new StepRegistry();
        registry.Register("Playwright.Test", () => Task.FromResult(true));

        // Act
        var ops = DindChecker.GetDindOperations(registry);

        // Assert
        ops.ShouldContain("Playwright.Test");
    }

    [Fact]
    public void ScanScriptAndChildrenForDind_DetectsDindOperationsInScript()
    {
        // Arrange - create a build script with DIND operations
        var scriptContent = @"
var project = Dotnet.Project(""./src/App/App.csproj"");
Dotnet.Build(project);
Docker.Build(""./Dockerfile"", o => o.WithTag(""myapp:latest""));
";
        var scriptPath = Path.Combine(_tempDir, "build.csando");
        File.WriteAllText(scriptPath, scriptContent);

        var checker = new DindChecker(_logger);

        // Act
        var ops = checker.ScanScriptAndChildrenForDind(scriptPath, _tempDir);

        // Assert
        ops.ShouldContain("Docker.Build");
    }

    [Fact]
    public void ScanScriptAndChildrenForDind_DetectsPlaywrightTestInScript()
    {
        // Arrange - create a build script with Playwright.Test
        var scriptContent = @"
var e2e = Directory(""./tests/E2E"");
Npm.Ci(e2e);
Playwright.Install(e2e);
Playwright.Test(e2e);
";
        var scriptPath = Path.Combine(_tempDir, "build.csando");
        File.WriteAllText(scriptPath, scriptContent);

        var checker = new DindChecker(_logger);

        // Act
        var ops = checker.ScanScriptAndChildrenForDind(scriptPath, _tempDir);

        // Assert
        ops.ShouldContain("Playwright.Test");
    }

    [Fact]
    public void ScanScriptAndChildrenForDind_ScansChildBuilds()
    {
        // Arrange - create parent script that calls a child build
        var parentScript = @"
Dotnet.Build(Dotnet.Project(""./App.csproj""));
Ando.Build(Directory(""./child""));
";
        var parentPath = Path.Combine(_tempDir, "build.csando");
        File.WriteAllText(parentPath, parentScript);

        // Create child directory and script with DIND operation
        var childDir = Path.Combine(_tempDir, "child");
        Directory.CreateDirectory(childDir);
        var childScript = @"
Docker.Build(""./Dockerfile"");
GitHub.PushImage(""myimage"");
";
        var childPath = Path.Combine(childDir, "build.csando");
        File.WriteAllText(childPath, childScript);

        var checker = new DindChecker(_logger);

        // Act
        var ops = checker.ScanScriptAndChildrenForDind(parentPath, _tempDir);

        // Assert - should find DIND ops from child build
        ops.ShouldContain("Docker.Build");
        ops.ShouldContain("GitHub.PushImage");
    }

    [Fact]
    public void ScanScriptAndChildrenForDind_HandlesNestedChildBuilds()
    {
        // Arrange - create parent -> child -> grandchild chain
        var parentScript = @"
Dotnet.Build(Dotnet.Project(""./App.csproj""));
Ando.Build(Directory(""./child""));
";
        File.WriteAllText(Path.Combine(_tempDir, "build.csando"), parentScript);

        // Child build
        var childDir = Path.Combine(_tempDir, "child");
        Directory.CreateDirectory(childDir);
        var childScript = @"
Npm.Ci(Directory("".""));
Ando.Build(Directory(""./grandchild""));
";
        File.WriteAllText(Path.Combine(childDir, "build.csando"), childScript);

        // Grandchild build with DIND operation
        var grandchildDir = Path.Combine(childDir, "grandchild");
        Directory.CreateDirectory(grandchildDir);
        var grandchildScript = @"
Docker.Install();
Playwright.Test(Directory("".""));
";
        File.WriteAllText(Path.Combine(grandchildDir, "build.csando"), grandchildScript);

        var checker = new DindChecker(_logger);

        // Act
        var ops = checker.ScanScriptAndChildrenForDind(
            Path.Combine(_tempDir, "build.csando"), _tempDir);

        // Assert - should find DIND ops from grandchild
        ops.ShouldContain("Docker.Install");
        ops.ShouldContain("Playwright.Test");
    }

    [Fact]
    public void ScanScriptAndChildrenForDind_HandlesCircularReferences()
    {
        // Arrange - create circular reference (parent -> child -> parent)
        var parentScript = @"
Docker.Build(""./Dockerfile"");
Ando.Build(Directory(""./child""));
";
        File.WriteAllText(Path.Combine(_tempDir, "build.csando"), parentScript);

        var childDir = Path.Combine(_tempDir, "child");
        Directory.CreateDirectory(childDir);
        var childScript = @"
// This creates a circular reference back to parent
Ando.Build(Directory(""..""));
";
        File.WriteAllText(Path.Combine(childDir, "build.csando"), childScript);

        var checker = new DindChecker(_logger);

        // Act - should not infinite loop
        var ops = checker.ScanScriptAndChildrenForDind(
            Path.Combine(_tempDir, "build.csando"), _tempDir);

        // Assert - should complete and find the DIND op
        ops.ShouldContain("Docker.Build");
    }

    [Fact]
    public void ScanScriptAndChildrenForDind_ReturnsEmpty_WhenNoDindOperations()
    {
        // Arrange - script with no DIND operations
        var scriptContent = @"
var project = Dotnet.Project(""./src/App/App.csproj"");
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(project);
";
        var scriptPath = Path.Combine(_tempDir, "build.csando");
        File.WriteAllText(scriptPath, scriptContent);

        var checker = new DindChecker(_logger);

        // Act
        var ops = checker.ScanScriptAndChildrenForDind(scriptPath, _tempDir);

        // Assert
        ops.ShouldBeEmpty();
    }

    [Fact]
    public void ScanScriptAndChildrenForDind_HandlesMissingChildScript()
    {
        // Arrange - parent references non-existent child
        var parentScript = @"
Docker.Build(""./Dockerfile"");
Ando.Build(Directory(""./nonexistent""));
";
        File.WriteAllText(Path.Combine(_tempDir, "build.csando"), parentScript);

        var checker = new DindChecker(_logger);

        // Act - should not throw
        var ops = checker.ScanScriptAndChildrenForDind(
            Path.Combine(_tempDir, "build.csando"), _tempDir);

        // Assert - should still find DIND op from parent
        ops.ShouldContain("Docker.Build");
    }

    [Fact]
    public void CheckAndPrompt_DetectsDindFromChildBuilds()
    {
        // Arrange - parent has no DIND ops, but child does
        var parentScript = @"
Dotnet.Build(Dotnet.Project(""./App.csproj""));
Ando.Build(Directory(""./e2e""));
";
        File.WriteAllText(Path.Combine(_tempDir, "build.csando"), parentScript);

        var childDir = Path.Combine(_tempDir, "e2e");
        Directory.CreateDirectory(childDir);
        var childScript = @"
Playwright.Test(Directory("".""));
";
        File.WriteAllText(Path.Combine(childDir, "build.csando"), childScript);

        var registry = new StepRegistry();
        registry.Register("Dotnet.Build", () => Task.FromResult(true));
        registry.Register("Ando.Build", () => Task.FromResult(true));

        var config = new ProjectConfig { Dind = true };
        config.Save(_tempDir);

        var checker = new DindChecker(_logger);

        // Act
        var result = checker.CheckAndPrompt(registry, hasDindFlag: false, _tempDir);

        // Assert - should detect DIND requirement from child and enable via config
        result.ShouldBe(DindCheckResult.EnabledViaConfig);
    }

    [Fact]
    public void CheckAndPrompt_ReturnsEnabledViaConfig_WhenAndoDindEnvVarSet()
    {
        // Arrange - set ANDO_DIND env var (simulating inheritance from parent build)
        Environment.SetEnvironmentVariable(DindChecker.DindEnvVar, "1");
        try
        {
            var registry = new StepRegistry();
            registry.Register("Docker.Build", () => Task.FromResult(true));
            var checker = new DindChecker(_logger);

            // Act
            var result = checker.CheckAndPrompt(registry, hasDindFlag: false, _tempDir);

            // Assert - should detect DIND from env var
            result.ShouldBe(DindCheckResult.EnabledViaConfig);
            _logger.DebugMessages.ShouldContain(m => m.Contains("ANDO_DIND environment variable"));
        }
        finally
        {
            // Clean up env var
            Environment.SetEnvironmentVariable(DindChecker.DindEnvVar, null);
        }
    }

    [Fact]
    public void CheckAndPrompt_EnvVarWithTrueValue_EnablesDind()
    {
        // Arrange - test "true" value (case-insensitive)
        Environment.SetEnvironmentVariable(DindChecker.DindEnvVar, "TRUE");
        try
        {
            var registry = new StepRegistry();
            registry.Register("Playwright.Test", () => Task.FromResult(true));
            var checker = new DindChecker(_logger);

            // Act
            var result = checker.CheckAndPrompt(registry, hasDindFlag: false, _tempDir);

            // Assert
            result.ShouldBe(DindCheckResult.EnabledViaConfig);
        }
        finally
        {
            Environment.SetEnvironmentVariable(DindChecker.DindEnvVar, null);
        }
    }

    [Fact]
    public void DindEnvVar_HasCorrectName()
    {
        // Assert - verify the constant has the expected value
        DindChecker.DindEnvVar.ShouldBe("ANDO_DIND");
    }
}
