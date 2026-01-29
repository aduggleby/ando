// =============================================================================
// ContainerExecutorTests.cs
//
// Summary: Unit tests for ContainerExecutor class.
//
// Tests verify that:
// - Docker exec commands are constructed correctly
// - Working directory is passed via -w flag
// - Environment variables are passed via -e flags
// - Container path conversion works correctly
// - SetHostRootPath enables host-to-container path translation
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Execution;

[Trait("Category", "Unit")]
public class ContainerExecutorTests
{
    private readonly TestLogger _logger = new();

    // Testable subclass that exposes the protected PrepareProcessStartInfo method
    private class TestableContainerExecutor : ContainerExecutor
    {
        public TestableContainerExecutor(string containerId, TestLogger logger, string containerWorkDir = "/workspace")
            : base(containerId, logger, containerWorkDir)
        {
        }

        public ProcessStartInfo TestPrepareProcessStartInfo(string command, string[] args, CommandOptions options)
            => PrepareProcessStartInfo(command, args, options);
    }

    [Fact]
    public void PrepareProcessStartInfo_SetsDockerAsFileName()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], new CommandOptions());

        startInfo.FileName.ShouldBe("docker");
    }

    [Fact]
    public void PrepareProcessStartInfo_AddsExecCommand()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], new CommandOptions());

        startInfo.ArgumentList[0].ShouldBe("exec");
    }

    [Fact]
    public void PrepareProcessStartInfo_AddsContainerId()
    {
        var executor = new TestableContainerExecutor("abc123", _logger);

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], new CommandOptions());

        startInfo.ArgumentList.ShouldContain("abc123");
    }

    [Fact]
    public void PrepareProcessStartInfo_AddsCommandAndArgs()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build", "-c", "Release"], new CommandOptions());

        startInfo.ArgumentList.ShouldContain("dotnet");
        startInfo.ArgumentList.ShouldContain("build");
        startInfo.ArgumentList.ShouldContain("-c");
        startInfo.ArgumentList.ShouldContain("Release");
    }

    [Fact]
    public void PrepareProcessStartInfo_AddsWorkingDirectoryFlag()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], new CommandOptions());

        startInfo.ArgumentList.ShouldContain("-w");
        startInfo.ArgumentList.ShouldContain("/workspace");
    }

    [Fact]
    public void PrepareProcessStartInfo_WithCustomWorkingDirectory_UsesProvidedPath()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);
        var options = new CommandOptions { WorkingDirectory = "/custom/path" };

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        startInfo.ArgumentList.ShouldContain("-w");
        startInfo.ArgumentList.ShouldContain("/custom/path");
    }

    [Fact]
    public void PrepareProcessStartInfo_WithEnvironmentVariables_AddsEnvFlags()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);
        var options = new CommandOptions
        {
            Environment = { ["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1", ["CI"] = "true" }
        };

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        // Check that -e flags are added
        var args = startInfo.ArgumentList.ToList();
        args.Count(a => a == "-e").ShouldBe(2);
        args.ShouldContain("DOTNET_CLI_TELEMETRY_OPTOUT=1");
        args.ShouldContain("CI=true");
    }

    [Fact]
    public void PrepareProcessStartInfo_ArgumentOrder_IsCorrect()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);
        var options = new CommandOptions
        {
            WorkingDirectory = "/app",
            Environment = { ["KEY"] = "value" }
        };

        var startInfo = executor.TestPrepareProcessStartInfo("npm", ["install"], options);

        var args = startInfo.ArgumentList.ToList();

        // Order should be: exec, -w, workdir, -e, env, container-id, command, args
        args[0].ShouldBe("exec");
        args[1].ShouldBe("-w");
        args[2].ShouldBe("/app");
        // Environment flags come next
        var containerIdIndex = args.IndexOf("my-container");
        containerIdIndex.ShouldBeGreaterThan(2);
        // Command comes after container ID
        args[containerIdIndex + 1].ShouldBe("npm");
        args[containerIdIndex + 2].ShouldBe("install");
    }

    [Fact]
    public void PrepareProcessStartInfo_WithCustomContainerWorkDir_UsesAsDefault()
    {
        var executor = new TestableContainerExecutor("my-container", _logger, containerWorkDir: "/app");

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], new CommandOptions());

        startInfo.ArgumentList.ShouldContain("-w");
        startInfo.ArgumentList.ShouldContain("/app");
    }

    [Fact]
    public void ConvertToContainerPath_WorkspacePath_ReturnsUnchanged()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);
        var options = new CommandOptions { WorkingDirectory = "/workspace/src/MyApp" };

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        startInfo.ArgumentList.ShouldContain("/workspace/src/MyApp");
    }

    [Fact]
    public void ConvertToContainerPath_RelativePath_PrependsWorkDir()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);
        var options = new CommandOptions { WorkingDirectory = "src/MyApp" };

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        startInfo.ArgumentList.ShouldContain("/workspace/src/MyApp");
    }

    [Fact]
    public void ConvertToContainerPath_AbsolutePath_ReturnsUnchanged()
    {
        var executor = new TestableContainerExecutor("my-container", _logger);
        var options = new CommandOptions { WorkingDirectory = "/some/other/path" };

        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        startInfo.ArgumentList.ShouldContain("/some/other/path");
    }

    // =============================================================================
    // SetHostRootPath Tests - Verify host-to-container path translation
    // =============================================================================

    [Fact]
    public void SetHostRootPath_WithHostPath_EnablesPathTranslation()
    {
        // Arrange - set up executor with host root path
        var executor = new TestableContainerExecutor("my-container", _logger);
        var hostRoot = Path.GetFullPath("/home/user/projects/myapp");
        executor.SetHostRootPath(hostRoot);

        // Act - use an absolute host path within the project
        var hostPath = Path.Combine(hostRoot, "src", "MyApp");
        var options = new CommandOptions { WorkingDirectory = hostPath };
        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        // Assert - path should be translated to container path
        startInfo.ArgumentList.ShouldContain("/workspace/src/MyApp");
    }

    [Fact]
    public void SetHostRootPath_WithNestedPath_TranslatesCorrectly()
    {
        // Arrange
        var executor = new TestableContainerExecutor("my-container", _logger);
        var hostRoot = Path.GetFullPath("/home/user/projects/myapp");
        executor.SetHostRootPath(hostRoot);

        // Act - use a deeply nested path
        var hostPath = Path.Combine(hostRoot, "src", "Ando", "Execution");
        var options = new CommandOptions { WorkingDirectory = hostPath };
        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        // Assert
        startInfo.ArgumentList.ShouldContain("/workspace/src/Ando/Execution");
    }

    [Fact]
    public void SetHostRootPath_WithPathOutsideProject_LogsWarning()
    {
        // Arrange
        var executor = new TestableContainerExecutor("my-container", _logger);
        executor.SetHostRootPath("/home/user/projects/myapp");

        // Act - use a path outside the project root
        var options = new CommandOptions { WorkingDirectory = "/home/user/other-project" };
        executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        // Assert - should log a warning about inaccessible path
        _logger.WarningMessages.ShouldContain(m => m.Contains("outside the project root"));
    }

    [Fact]
    public void SetHostRootPath_NotCalled_AbsolutePathUnchanged()
    {
        // Arrange - do NOT call SetHostRootPath
        var executor = new TestableContainerExecutor("my-container", _logger);

        // Act - use an absolute path (without host root set, it stays unchanged)
        var options = new CommandOptions { WorkingDirectory = "/home/user/projects/myapp/src" };
        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        // Assert - path should not be translated (no host root set)
        startInfo.ArgumentList.ShouldContain("/home/user/projects/myapp/src");
    }

    [Fact]
    public void SetHostRootPath_WithContainerPath_NotTranslated()
    {
        // Arrange
        var executor = new TestableContainerExecutor("my-container", _logger);
        executor.SetHostRootPath("/home/user/projects/myapp");

        // Act - use a path already in container format
        var options = new CommandOptions { WorkingDirectory = "/workspace/src/MyApp" };
        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        // Assert - already a container path, should not be changed
        startInfo.ArgumentList.ShouldContain("/workspace/src/MyApp");
    }

    [Fact]
    public void SetHostRootPath_WithRelativePath_PrependsWorkDir()
    {
        // Arrange
        var executor = new TestableContainerExecutor("my-container", _logger);
        executor.SetHostRootPath("/home/user/projects/myapp");

        // Act - relative paths should still prepend workdir
        var options = new CommandOptions { WorkingDirectory = "src/MyApp" };
        var startInfo = executor.TestPrepareProcessStartInfo("dotnet", ["build"], options);

        // Assert - relative paths prepend container workdir
        startInfo.ArgumentList.ShouldContain("/workspace/src/MyApp");
    }
}
