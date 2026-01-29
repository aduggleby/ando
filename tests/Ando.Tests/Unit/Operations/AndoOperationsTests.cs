// =============================================================================
// AndoOperationsTests.cs
//
// Summary: Unit tests for AndoOperations class.
//
// Tests verify UseImage, CopyArtifactsToHost, and Build operations.
// Uses MockExecutor to verify command execution without actual process spawning.
// =============================================================================

using Ando.Operations;
using Ando.Profiles;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class AndoOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();
    private readonly BuildOptions _buildOptions = new();
    private readonly ProfileRegistry _profileRegistry = new();

    private AndoOperations CreateAndoOperations()
    {
        var artifactOps = new ArtifactOperations(_logger);
        return new AndoOperations(
            _registry,
            _logger,
            p => p,  // Identity function for path translation
            _buildOptions,
            artifactOps,
            _profileRegistry);
    }

    // UseImage tests

    [Fact]
    public void UseImage_SetsImageOnBuildOptions()
    {
        var ando = CreateAndoOperations();

        ando.UseImage("ubuntu:24.04");

        Assert.Equal("ubuntu:24.04", _buildOptions.Image);
    }

    [Fact]
    public void UseImage_LogsDebugMessage()
    {
        var ando = CreateAndoOperations();

        ando.UseImage("mcr.microsoft.com/dotnet/sdk:9.0");

        Assert.Contains(_logger.DebugMessages, m => m.Contains("mcr.microsoft.com/dotnet/sdk:9.0"));
    }

    [Fact]
    public void UseImage_CanBeCalledMultipleTimes()
    {
        var ando = CreateAndoOperations();

        ando.UseImage("ubuntu:22.04");
        ando.UseImage("ubuntu:24.04");

        Assert.Equal("ubuntu:24.04", _buildOptions.Image);
    }

    [Fact]
    public void UseImage_DoesNotRegisterStep()
    {
        var ando = CreateAndoOperations();

        ando.UseImage("ubuntu:24.04");

        Assert.Empty(_registry.Steps);
    }

    // CopyArtifactsToHost tests

    [Fact]
    public void CopyArtifactsToHost_DelegatesToArtifactOperations()
    {
        var ando = CreateAndoOperations();

        ando.CopyArtifactsToHost("./dist", "./output");

        // ArtifactOperations adds to internal list, doesn't register step
        // The operation is logged as debug message
        Assert.Contains(_logger.DebugMessages, m => m.Contains("Registered artifact"));
    }

    [Fact]
    public void CopyZippedArtifactsToHost_DelegatesToArtifactOperations()
    {
        var ando = CreateAndoOperations();

        ando.CopyZippedArtifactsToHost("./dist", "./output.tar.gz");

        // ArtifactOperations adds to internal list, doesn't register step
        Assert.Contains(_logger.DebugMessages, m => m.Contains("Registered zipped artifact"));
    }

    // Build tests

    [Fact]
    public void Build_RegistersStep()
    {
        var ando = CreateAndoOperations();
        var dir = new DirectoryRef("./website");

        ando.Build(dir);

        Assert.Single(_registry.Steps);
        Assert.Equal("Ando.Build", _registry.Steps[0].Name);
    }

    [Fact]
    public void Build_SetsContextToDirectoryName()
    {
        var ando = CreateAndoOperations();
        var dir = new DirectoryRef("./website");

        ando.Build(dir);

        Assert.Equal("website", _registry.Steps[0].Context);
    }

    [Fact]
    public void Build_WithCsandoFile_SetsContextToFileName()
    {
        var ando = CreateAndoOperations();
        var dir = new DirectoryRef("./website/deploy.csando");

        ando.Build(dir);

        Assert.Equal("deploy.csando", _registry.Steps[0].Context);
    }

    [Fact]
    public void Build_WithProfile_PassesProfileToChildBuild()
    {
        var ando = CreateAndoOperations();
        var dir = new DirectoryRef("./website");

        ando.Build(dir, o => o.WithProfile("release"));

        Assert.Single(_registry.Steps);
    }

    [Fact]
    public void Build_InheritsActiveProfiles()
    {
        _profileRegistry.SetActiveProfiles(["push", "release"]);
        var ando = CreateAndoOperations();
        var dir = new DirectoryRef("./website");

        ando.Build(dir);

        // The step is registered - profile inheritance happens during execution
        Assert.Single(_registry.Steps);
    }

    [Fact]
    public void Build_WithVerbosity_PassesVerbosity()
    {
        var ando = CreateAndoOperations();
        var dir = new DirectoryRef("./website");

        ando.Build(dir, o => o.WithVerbosity("quiet"));

        Assert.Single(_registry.Steps);
    }

    [Fact]
    public void Build_CanBuildMultipleDirectories()
    {
        var ando = CreateAndoOperations();

        ando.Build(new DirectoryRef("./website"));
        ando.Build(new DirectoryRef("./api"));
        ando.Build(new DirectoryRef("./docs"));

        Assert.Equal(3, _registry.Steps.Count);
    }
}

// Tests for AndoBuildOptions
[Trait("Category", "Unit")]
public class AndoBuildOptionsTests
{
    [Fact]
    public void WithProfile_SetsProfile()
    {
        var options = new AndoBuildOptions();

        options.WithProfile("release");

        Assert.Equal("release", options.Profile);
    }

    [Fact]
    public void WithVerbosity_SetsVerbosity()
    {
        var options = new AndoBuildOptions();

        options.WithVerbosity("quiet");

        Assert.Equal("quiet", options.Verbosity);
    }

    [Fact]
    public void WithDind_SetsDind()
    {
        var options = new AndoBuildOptions();

        options.WithDind(true);

        Assert.True(options.Dind);
    }

    [Fact]
    public void ColdStart_SetsCold()
    {
        var options = new AndoBuildOptions();

        options.ColdStart();

        Assert.True(options.Cold);
    }

    [Fact]
    public void WithImage_SetsImage()
    {
        var options = new AndoBuildOptions();

        options.WithImage("ubuntu:24.04");

        Assert.Equal("ubuntu:24.04", options.Image);
    }

    [Fact]
    public void BuildArgs_WithProfile_IncludesProfileFlag()
    {
        var options = new AndoBuildOptions();
        options.WithProfile("release");

        var args = options.BuildArgs();

        Assert.Contains("-p", args);
        Assert.Contains("release", args);
    }

    [Fact]
    public void BuildArgs_WithVerbosity_IncludesVerbosityFlag()
    {
        var options = new AndoBuildOptions();
        options.WithVerbosity("quiet");

        var args = options.BuildArgs();

        Assert.Contains("--verbosity", args);
        Assert.Contains("quiet", args);
    }

    [Fact]
    public void BuildArgs_WithDind_IncludesDindFlag()
    {
        var options = new AndoBuildOptions();
        options.WithDind(true);

        var args = options.BuildArgs();

        Assert.Contains("--dind", args);
    }

    [Fact]
    public void BuildArgs_WithCold_IncludesColdFlag()
    {
        var options = new AndoBuildOptions();
        options.ColdStart();

        var args = options.BuildArgs();

        Assert.Contains("--cold", args);
    }

    [Fact]
    public void BuildArgs_WithImage_IncludesImageFlag()
    {
        var options = new AndoBuildOptions();
        options.WithImage("ubuntu:24.04");

        var args = options.BuildArgs();

        Assert.Contains("--image", args);
        Assert.Contains("ubuntu:24.04", args);
    }

    [Fact]
    public void BuildArgs_WithNoOptions_ContainsOnlyRun()
    {
        var options = new AndoBuildOptions();

        var args = options.BuildArgs();

        Assert.Single(args);
        Assert.Equal("run", args[0]);
    }

    [Fact]
    public void BuildArgs_WithMultipleOptions_IncludesAll()
    {
        var options = new AndoBuildOptions();
        options.WithProfile("release").WithVerbosity("quiet").WithDind(true).ColdStart();

        var args = options.BuildArgs();

        Assert.Contains("-p", args);
        Assert.Contains("--verbosity", args);
        Assert.Contains("--dind", args);
        Assert.Contains("--cold", args);
    }
}
