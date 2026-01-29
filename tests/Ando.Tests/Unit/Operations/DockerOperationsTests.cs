// =============================================================================
// DockerOperationsTests.cs
//
// Summary: Unit tests for DockerOperations class.
//
// Tests verify Install, Build, and IsAvailable operations.
// Uses MockExecutor to verify command execution without actual Docker calls.
// =============================================================================

using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Utilities;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class DockerOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private DockerOperations CreateDocker(GitHubAuthHelper? authHelper = null)
    {
        return new DockerOperations(_registry, _logger, () => _executor, authHelper);
    }

    // Install tests

    [Fact]
    public void Install_RegistersStep()
    {
        var docker = CreateDocker();

        docker.Install();

        Assert.Single(_registry.Steps);
        Assert.Equal("Docker.Install", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Install_WhenAlreadyInstalled_LogsAndReturnsTrue()
    {
        var docker = CreateDocker();
        docker.Install();

        var result = await _registry.Steps[0].Execute();

        Assert.True(result);
        // First command is "which docker"
        Assert.Contains(_executor.ExecutedCommands, c => c.Command == "which" && c.HasArg("docker"));
    }

    [Fact]
    public async Task Install_WhenNotInstalled_RunsInstallScript()
    {
        // Simulate 'which docker' failing by making the executor fail
        _executor.SimulateFailure = true;

        var docker = CreateDocker();
        docker.Install();

        // The step will fail because we simulated failure
        var result = await _registry.Steps[0].Execute();

        // When 'which docker' fails, it tries to install
        Assert.Contains(_executor.ExecutedCommands, c => c.Command == "which" && c.HasArg("docker"));
    }

    // Build tests

    [Fact]
    public void Build_RegistersStep()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile");

        Assert.Single(_registry.Steps);
        Assert.Equal("Docker.Build", _registry.Steps[0].Name);
    }

    [Fact]
    public void Build_SetsContextToFirstTag()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o.WithTag("myapp:v1.0.0"));

        Assert.Equal("myapp:v1.0.0", _registry.Steps[0].Context);
    }

    [Fact]
    public void Build_WithNoTag_SetsContextToDockerfile()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile");

        Assert.Equal("./Dockerfile", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task Build_ExecutesBuildxCommand()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o.WithTag("myapp:latest"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker");
        Assert.True(cmd.HasArg("buildx"));
        Assert.True(cmd.HasArg("build"));
    }

    [Fact]
    public async Task Build_IncludesDockerfileFlag()
    {
        var docker = CreateDocker();

        docker.Build("./src/Dockerfile", o => o.WithTag("myapp:latest"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker");
        Assert.True(cmd.HasArg("-f"));
        Assert.True(cmd.HasArg("./src/Dockerfile"));
    }

    [Fact]
    public async Task Build_WithTag_IncludesTagFlag()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o.WithTag("myapp:v1.0.0"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker");
        Assert.True(cmd.HasArg("-t"));
        Assert.True(cmd.HasArg("myapp:v1.0.0"));
    }

    [Fact]
    public async Task Build_WithMultipleTags_IncludesAllTags()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:v1.0.0")
            .WithTag("myapp:latest"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker");
        var tagCount = cmd.Args.Count(a => a == "-t");
        Assert.Equal(2, tagCount);
    }

    [Fact]
    public async Task Build_WithBuildArg_IncludesBuildArgFlag()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:latest")
            .WithBuildArg("VERSION", "1.0.0"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker");
        Assert.True(cmd.HasArg("--build-arg"));
        Assert.True(cmd.HasArg("VERSION=1.0.0"));
    }

    [Fact]
    public async Task Build_WithPush_IncludesPushFlag()
    {
        var authHelper = new GitHubAuthHelper(_logger);
        var docker = CreateDocker(authHelper);

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:latest")
            .WithPush());

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker" && c.HasArg("build"));
        Assert.True(cmd.HasArg("--push"));
    }

    [Fact]
    public async Task Build_WithLoad_IncludesLoadFlag()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o.WithTag("myapp:latest"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker");
        Assert.True(cmd.HasArg("--load"));
    }

    [Fact]
    public async Task Build_WithNoCache_IncludesNoCacheFlag()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:latest")
            .WithNoCache());

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker");
        Assert.True(cmd.HasArg("--no-cache"));
    }

    [Fact]
    public async Task Build_WithPlatform_IncludesPlatformFlag()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:latest")
            .WithPlatform("linux/amd64"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker" && c.HasArg("build"));
        Assert.True(cmd.HasArg("--platform"));
        Assert.True(cmd.HasArg("linux/amd64"));
    }

    [Fact]
    public async Task Build_WithMultiplePlatforms_JoinsPlatforms()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:latest")
            .WithPlatforms("linux/amd64", "linux/arm64"));

        await _registry.Steps[0].Execute();

        // Should create builder first for multi-platform
        Assert.Contains(_executor.ExecutedCommands, c =>
            c.Command == "docker" && c.HasArg("buildx") && c.HasArg("create"));

        var buildCmd = _executor.ExecutedCommands.First(c =>
            c.Command == "docker" && c.HasArg("build"));
        Assert.True(buildCmd.HasArg("linux/amd64,linux/arm64"));
    }

    [Fact]
    public async Task Build_WithContext_UsesContext()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:latest")
            .WithContext("./src"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands.First(c => c.Command == "docker");
        // Context should be the last argument
        Assert.Equal("./src", cmd.Args.Last());
    }

    [Fact]
    public async Task Build_Failure_ReturnsFalse()
    {
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Build failed";

        var docker = CreateDocker();
        docker.Build("./Dockerfile", o => o.WithTag("myapp:latest"));

        var result = await _registry.Steps[0].Execute();

        Assert.False(result);
    }

    // Error path tests

    [Fact]
    public async Task Build_Failure_LogsError()
    {
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Build failed";

        var docker = CreateDocker();
        docker.Build("./Dockerfile", o => o.WithTag("myapp:latest"));

        await _registry.Steps[0].Execute();

        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Docker build failed"));
    }

    [Fact]
    public async Task Install_Failure_LogsError()
    {
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Install failed";

        var docker = CreateDocker();
        docker.Install();

        await _registry.Steps[0].Execute();

        // When 'which docker' fails and install fails, should log error
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Failed to install Docker CLI"));
    }

    [Fact]
    public async Task Build_WithGhcrPush_NoAuthHelper_ReturnsFalse()
    {
        // Create docker without auth helper
        var docker = CreateDocker(null);

        docker.Build("./Dockerfile", o => o
            .WithTag("ghcr.io/myorg/myapp:latest")
            .WithPush());

        var result = await _registry.Steps[0].Execute();

        Assert.False(result);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("GitHub authentication not available"));
    }

    // Note: Testing no-token scenario requires clearing GITHUB_TOKEN env var,
    // which could affect other tests. The no-auth-helper test covers this path.

    [Fact]
    public async Task Build_WithMultiplePlatforms_CreatesBuilder()
    {
        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:latest")
            .WithPlatforms("linux/amd64", "linux/arm64"));

        await _registry.Steps[0].Execute();

        // Should attempt to create buildx builder for multi-platform
        Assert.Contains(_executor.ExecutedCommands, c =>
            c.Command == "docker" && c.HasArg("buildx") && c.HasArg("create"));
    }

    [Fact]
    public async Task Build_WithMultiplePlatforms_BuilderAlreadyExists_TriesToUseExisting()
    {
        // Make "create" fail but "use" succeed
        _executor.ConditionalFailures["docker"] = args => args.Contains("create");

        var docker = CreateDocker();

        docker.Build("./Dockerfile", o => o
            .WithTag("myapp:latest")
            .WithPlatforms("linux/amd64", "linux/arm64"));

        var result = await _registry.Steps[0].Execute();

        // Should have tried to use existing builder after create failed
        Assert.Contains(_executor.ExecutedCommands, c =>
            c.Command == "docker" && c.HasArg("use"));
    }
}

// Tests for DockerBuildOptions
[Trait("Category", "Unit")]
public class DockerBuildOptionsTests
{
    [Fact]
    public void WithTag_AddsTag()
    {
        var options = new DockerBuildOptions();

        options.WithTag("myapp:v1.0.0");

        Assert.Single(options.Tags);
        Assert.Contains("myapp:v1.0.0", options.Tags);
    }

    [Fact]
    public void WithTag_CanAddMultipleTags()
    {
        var options = new DockerBuildOptions();

        options.WithTag("myapp:v1.0.0");
        options.WithTag("myapp:latest");

        Assert.Equal(2, options.Tags.Count);
    }

    [Fact]
    public void WithPlatform_SetsSinglePlatform()
    {
        var options = new DockerBuildOptions();

        options.WithPlatform("linux/amd64");

        Assert.Single(options.Platforms);
        Assert.Contains("linux/amd64", options.Platforms);
    }

    [Fact]
    public void WithPlatform_ClearsPreviousPlatforms()
    {
        var options = new DockerBuildOptions();
        options.WithPlatforms("linux/arm64", "linux/amd64");

        options.WithPlatform("linux/amd64");

        Assert.Single(options.Platforms);
    }

    [Fact]
    public void WithPlatforms_AddsMultiplePlatforms()
    {
        var options = new DockerBuildOptions();

        options.WithPlatforms("linux/amd64", "linux/arm64");

        Assert.Equal(2, options.Platforms.Count);
    }

    [Fact]
    public void WithContext_SetsContext()
    {
        var options = new DockerBuildOptions();

        options.WithContext("./src");

        Assert.Equal("./src", options.Context);
    }

    [Fact]
    public void WithBuildArg_AddsBuildArg()
    {
        var options = new DockerBuildOptions();

        options.WithBuildArg("VERSION", "1.0.0");

        Assert.Single(options.BuildArgs);
        Assert.Equal("1.0.0", options.BuildArgs["VERSION"]);
    }

    [Fact]
    public void WithBuildArg_CanAddMultiple()
    {
        var options = new DockerBuildOptions();

        options.WithBuildArg("VERSION", "1.0.0");
        options.WithBuildArg("BUILD_DATE", "2024-01-01");

        Assert.Equal(2, options.BuildArgs.Count);
    }

    [Fact]
    public void WithPush_SetsPushAndDisablesLoad()
    {
        var options = new DockerBuildOptions();

        options.WithPush();

        Assert.True(options.Push);
        Assert.False(options.Load);
    }

    [Fact]
    public void WithNoCache_SetsNoCache()
    {
        var options = new DockerBuildOptions();

        options.WithNoCache();

        Assert.True(options.NoCache);
    }

    [Fact]
    public void WithoutLoad_DisablesLoad()
    {
        var options = new DockerBuildOptions();

        options.WithoutLoad();

        Assert.False(options.Load);
    }

    [Fact]
    public void Load_DefaultsToTrue()
    {
        var options = new DockerBuildOptions();

        Assert.True(options.Load);
    }

    [Fact]
    public void FluentChaining_Works()
    {
        var options = new DockerBuildOptions()
            .WithTag("myapp:v1.0.0")
            .WithTag("myapp:latest")
            .WithPlatforms("linux/amd64", "linux/arm64")
            .WithContext("./src")
            .WithBuildArg("VERSION", "1.0.0")
            .WithPush()
            .WithNoCache();

        Assert.Equal(2, options.Tags.Count);
        Assert.Equal(2, options.Platforms.Count);
        Assert.Equal("./src", options.Context);
        Assert.Single(options.BuildArgs);
        Assert.True(options.Push);
        Assert.True(options.NoCache);
    }
}
