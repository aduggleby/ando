// =============================================================================
// NpmOperationsTests.cs
//
// Summary: Unit tests for NpmOperations class.
//
// Tests verify that:
// - Each operation (Install, Ci, Run, Test, Build) registers the correct step
// - InDirectory/InProject correctly sets working directory
// - Steps execute the correct npm commands with proper arguments
// - Working directory is passed via CommandOptions
// =============================================================================

using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class NpmOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private NpmOperations CreateNpm() =>
        new NpmOperations(_registry, _logger, () => _executor);

    [Fact]
    public void Install_RegistersStep()
    {
        var npm = CreateNpm();

        npm.Install();

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Install", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Install_ExecutesNpmInstall()
    {
        var npm = CreateNpm();

        npm.Install();
        await _registry.Steps[0].Execute();

        Assert.Single(_executor.ExecutedCommands);
        _executor.WasExecuted("npm", "install").ShouldBeTrue();
    }

    [Fact]
    public void Ci_RegistersStep()
    {
        var npm = CreateNpm();

        npm.Ci();

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Ci", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Ci_ExecutesNpmCi()
    {
        var npm = CreateNpm();

        npm.Ci();
        await _registry.Steps[0].Execute();

        _executor.WasExecuted("npm", "ci").ShouldBeTrue();
    }

    [Fact]
    public void Run_RegistersStepWithScriptName()
    {
        var npm = CreateNpm();

        npm.Run("lint");

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Run(lint)", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Run_ExecutesNpmRunWithScriptName()
    {
        var npm = CreateNpm();

        npm.Run("lint");
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("npm");
        cmd.HasArg("run").ShouldBeTrue();
        cmd.HasArg("lint").ShouldBeTrue();
    }

    [Fact]
    public void Test_RegistersStep()
    {
        var npm = CreateNpm();

        npm.Test();

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Test", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Test_ExecutesNpmTest()
    {
        var npm = CreateNpm();

        npm.Test();
        await _registry.Steps[0].Execute();

        _executor.WasExecuted("npm", "test").ShouldBeTrue();
    }

    [Fact]
    public void Build_RegistersStep()
    {
        var npm = CreateNpm();

        npm.Build();

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Build", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Build_ExecutesNpmRunBuild()
    {
        var npm = CreateNpm();

        npm.Build();
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("npm");
        cmd.HasArg("run").ShouldBeTrue();
        cmd.HasArg("build").ShouldBeTrue();
    }

    [Fact]
    public void InDirectory_SetsWorkingDirectory()
    {
        var npm = CreateNpm();

        npm.InDirectory("/path/to/project").Install();

        Assert.Equal("/path/to/project", _registry.Steps[0].Context);
    }

    [Fact]
    public void InProject_SetsWorkingDirectoryFromProject()
    {
        var npm = CreateNpm();
        var project = ProjectRef.From("./src/frontend/frontend.csproj");

        npm.InProject(project).Install();

        Assert.Equal("./src/frontend", _registry.Steps[0].Context);
    }

    [Fact]
    public void InDirectory_ReturnsSameInstance_ForChaining()
    {
        var npm = CreateNpm();

        var result = npm.InDirectory("/path");

        result.ShouldBeSameAs(npm);
    }

    [Fact]
    public void InProject_ReturnsSameInstance_ForChaining()
    {
        var npm = CreateNpm();
        var project = ProjectRef.From("./src/app/app.csproj");

        var result = npm.InProject(project);

        result.ShouldBeSameAs(npm);
    }

    [Fact]
    public async Task AllOperations_UseWorkingDirectory()
    {
        var npm = CreateNpm();
        var workDir = "/custom/path";

        npm.InDirectory(workDir);
        npm.Install();
        npm.Ci();
        npm.Run("test");
        npm.Test();
        npm.Build();

        // Execute all steps
        foreach (var step in _registry.Steps)
        {
            await step.Execute();
        }

        // All commands should have the same working directory context
        _registry.Steps.Count.ShouldBe(5);
        foreach (var step in _registry.Steps)
        {
            step.Context.ShouldBe(workDir);
        }
    }

    [Fact]
    public async Task CommandFailure_ReturnsFalse()
    {
        var npm = CreateNpm();
        _executor.SimulateFailure = true;

        npm.Install();
        var result = await _registry.Steps[0].Execute();

        result.ShouldBeFalse();
    }
}
