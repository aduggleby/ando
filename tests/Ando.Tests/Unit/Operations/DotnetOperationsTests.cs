// =============================================================================
// DotnetOperationsTests.cs
//
// Summary: Unit tests for DotnetOperations class.
//
// Tests verify that:
// - Each operation (Restore, Build, Test, Publish) registers the correct step
// - Steps execute the correct dotnet commands with proper arguments
// - Options (Configuration, Runtime, etc.) are translated to CLI flags
// - Failure cases return false without throwing
//
// Design: Uses MockExecutor to capture commands without execution,
// and TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class DotnetOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private DotnetOperations CreateDotnet() =>
        new DotnetOperations(_registry, _logger, () => _executor);

    [Fact]
    public void Restore_RegistersStep()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Restore(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Dotnet.Restore", _registry.Steps[0].Name);
        Assert.Equal("MyApp", _registry.Steps[0].Context);
    }

    [Fact]
    public void Build_RegistersStep()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Build(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Dotnet.Build", _registry.Steps[0].Name);
    }

    [Fact]
    public void Test_RegistersStep()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Test(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Dotnet.Test", _registry.Steps[0].Name);
    }

    [Fact]
    public void Publish_RegistersStep()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Publish(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Dotnet.Publish", _registry.Steps[0].Name);
    }

    [Fact]
    public void Publish_WithOptions_AcceptsConfiguration()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Publish(project, o => o.WithConfiguration(Configuration.Release));

        Assert.Single(_registry.Steps);
    }

    [Fact]
    public async Task AllOperations_ExecuteSuccessfully()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Restore(project);
        dotnet.Build(project);
        dotnet.Test(project);
        dotnet.Publish(project);

        foreach (var step in _registry.Steps)
        {
            var result = await step.Execute();
            Assert.True(result);
        }
    }

    [Fact]
    public async Task Build_ExecutesCorrectCommand()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Build(project, o => o.Configuration = Configuration.Release);

        await _registry.Steps[0].Execute();

        Assert.Single(_executor.ExecutedCommands);
        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("build", args);
        Assert.Contains("-c", args);
        Assert.Contains("Release", args);
    }

    [Fact]
    public async Task Build_Failure_ReturnsFalse()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Build failed";

        dotnet.Build(project);

        var result = await _registry.Steps[0].Execute();

        result.ShouldBeFalse();
        // The operation doesn't log errors, it just returns false
        // Error logging is handled by the workflow runner
    }

    [Fact]
    public async Task Build_WithConfiguration_UsesCorrectFlag()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Build(project, o => o.Configuration = Configuration.Debug);

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        Assert.Equal("Debug", cmd.GetArgValue("-c"));
    }

    [Fact]
    public async Task Restore_ExecutesCorrectCommand()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Restore(project);

        await _registry.Steps[0].Execute();

        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("restore", args);
        Assert.Contains("./src/MyApp/MyApp.csproj", args);
    }

    [Fact]
    public async Task Test_ExecutesCorrectCommand()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp.Tests/MyApp.Tests.csproj");

        dotnet.Test(project);

        await _registry.Steps[0].Execute();

        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("test", args);
        Assert.Contains("./src/MyApp.Tests/MyApp.Tests.csproj", args);
    }

    [Fact]
    public async Task Publish_ExecutesCorrectCommand()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Publish(project, o => o
            .WithConfiguration(Configuration.Release)
            .WithRuntime("linux-x64")
            .AsSelfContained());

        await _registry.Steps[0].Execute();

        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("publish", args);
        Assert.Contains("Release", args);
        Assert.Contains("linux-x64", args);
        Assert.Contains("--self-contained", args);
    }

    // DotnetTool tests

    [Fact]
    public void Tool_CreatesToolWithPackageId()
    {
        var dotnet = CreateDotnet();

        var tool = dotnet.Tool("dotnet-ef");

        tool.PackageId.ShouldBe("dotnet-ef");
    }

    [Fact]
    public void Tool_CreatesToolWithVersion()
    {
        var dotnet = CreateDotnet();

        var tool = dotnet.Tool("dotnet-ef", "8.0.0");

        tool.PackageId.ShouldBe("dotnet-ef");
        tool.Version.ShouldBe("8.0.0");
    }

    [Fact]
    public void Tool_WithoutVersion_HasNullVersion()
    {
        var dotnet = CreateDotnet();

        var tool = dotnet.Tool("dotnet-ef");

        tool.Version.ShouldBeNull();
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_ExecutesDotnetToolInstall()
    {
        var dotnet = CreateDotnet();
        var tool = dotnet.Tool("dotnet-ef");

        await tool.EnsureInstalledAsync();

        _executor.ExecutedCommands.ShouldHaveSingleItem();
        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("dotnet");
        cmd.HasArg("tool").ShouldBeTrue();
        cmd.HasArg("install").ShouldBeTrue();
        cmd.HasArg("dotnet-ef").ShouldBeTrue();
        cmd.HasArg("--global").ShouldBeTrue();
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_IncludesVersionWhenSpecified()
    {
        var dotnet = CreateDotnet();
        var tool = dotnet.Tool("dotnet-ef", "8.0.0");

        await tool.EnsureInstalledAsync();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--version").ShouldBeTrue();
        cmd.HasArg("8.0.0").ShouldBeTrue();
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_OnlyInstallsOnce()
    {
        var dotnet = CreateDotnet();
        var tool = dotnet.Tool("dotnet-ef");

        await tool.EnsureInstalledAsync();
        await tool.EnsureInstalledAsync();
        await tool.EnsureInstalledAsync();

        // Should only execute once
        _executor.ExecutedCommands.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_LogsInstallation()
    {
        var dotnet = CreateDotnet();
        var tool = dotnet.Tool("dotnet-ef", "8.0.0");

        await tool.EnsureInstalledAsync();

        _logger.InfoMessages.ShouldContain(m => m.Contains("dotnet-ef") && m.Contains("v8.0.0"));
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_OnFailure_LogsWarning()
    {
        var dotnet = CreateDotnet();
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Installation failed";
        var tool = dotnet.Tool("dotnet-ef");

        await tool.EnsureInstalledAsync();

        _logger.WarningMessages.ShouldContain(m => m.Contains("dotnet-ef"));
    }
}
