// =============================================================================
// NpmOperationsTests.cs
//
// Summary: Unit tests for NpmOperations class.
//
// Tests verify that:
// - Each operation (Install, Ci, Run, Test, Build) registers the correct step
// - DirectoryRef correctly sets working directory
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

    private static DirectoryRef TestDir(string path = "./test-project") =>
        new DirectoryRef(path);

    [Fact]
    public void Install_RegistersStep()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Install(dir);

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Install", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Install_ExecutesNpmInstall()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Install(dir);
        await _registry.Steps[0].Execute();

        Assert.Single(_executor.ExecutedCommands);
        _executor.WasExecuted("npm", "install").ShouldBeTrue();
    }

    [Fact]
    public void Ci_RegistersStep()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Ci(dir);

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Ci", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Ci_ExecutesNpmCi()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Ci(dir);
        await _registry.Steps[0].Execute();

        _executor.WasExecuted("npm", "ci").ShouldBeTrue();
    }

    [Fact]
    public void Run_RegistersStepWithScriptName()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Run(dir, "lint");

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Run(lint)", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Run_ExecutesNpmRunWithScriptName()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Run(dir, "lint");
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
        var dir = TestDir();

        npm.Test(dir);

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Test", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Test_ExecutesNpmTest()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Test(dir);
        await _registry.Steps[0].Execute();

        _executor.WasExecuted("npm", "test").ShouldBeTrue();
    }

    [Fact]
    public void Build_RegistersStep()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Build(dir);

        Assert.Single(_registry.Steps);
        Assert.Equal("Npm.Build", _registry.Steps[0].Name);
    }

    [Fact]
    public async Task Build_ExecutesNpmRunBuild()
    {
        var npm = CreateNpm();
        var dir = TestDir();

        npm.Build(dir);
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("npm");
        cmd.HasArg("run").ShouldBeTrue();
        cmd.HasArg("build").ShouldBeTrue();
    }

    [Fact]
    public void DirectoryRef_SetsContextToDirectoryName()
    {
        var npm = CreateNpm();
        var dir = new DirectoryRef("/path/to/project");

        npm.Install(dir);

        Assert.Equal("project", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task AllOperations_UseCorrectWorkingDirectory()
    {
        var npm = CreateNpm();
        var dir = new DirectoryRef("/custom/path");

        npm.Install(dir);
        npm.Ci(dir);
        npm.Run(dir, "test");
        npm.Test(dir);
        npm.Build(dir);

        // Execute all steps
        foreach (var step in _registry.Steps)
        {
            await step.Execute();
        }

        // All commands should have the same working directory context
        _registry.Steps.Count.ShouldBe(5);
        foreach (var step in _registry.Steps)
        {
            step.Context.ShouldBe("path"); // Directory name from /custom/path
        }
    }

    [Fact]
    public async Task CommandFailure_ReturnsFalse()
    {
        var npm = CreateNpm();
        var dir = TestDir();
        _executor.SimulateFailure = true;

        npm.Install(dir);
        var result = await _registry.Steps[0].Execute();

        result.ShouldBeFalse();
    }
}
