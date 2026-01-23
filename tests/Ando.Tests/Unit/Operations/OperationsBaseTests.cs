// =============================================================================
// OperationsBaseTests.cs
//
// Summary: Unit tests for OperationsBase abstract class.
//
// Tests verify that:
// - RegisterCommand correctly registers steps with the registry
// - Steps execute commands with proper arguments and options
// - Working directory is passed via CommandOptions when specified
// - Builder function overload correctly builds arguments
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class OperationsBaseTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    // Concrete implementation for testing the abstract base class
    private class TestOperations : OperationsBase
    {
        public TestOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
            : base(registry, logger, executorFactory)
        {
        }

        public void RegisterWithArgs(string stepName, string command, string[] args, string? context = null, string? workDir = null)
            => RegisterCommand(stepName, command, args, context, workDir);

        public void RegisterWithBuilder(string stepName, string command, Func<ArgumentBuilder> builder, string? context = null, string? workDir = null)
            => RegisterCommand(stepName, command, builder, context, workDir);
    }

    private TestOperations CreateTestOps() =>
        new TestOperations(_registry, _logger, () => _executor);

    [Fact]
    public void RegisterCommand_WithArgs_RegistersStep()
    {
        var ops = CreateTestOps();

        ops.RegisterWithArgs("Test.Step", "testcmd", ["arg1", "arg2"]);

        _registry.Steps.ShouldHaveSingleItem();
        _registry.Steps[0].Name.ShouldBe("Test.Step");
    }

    [Fact]
    public void RegisterCommand_WithContext_SetsStepContext()
    {
        var ops = CreateTestOps();

        ops.RegisterWithArgs("Test.Step", "testcmd", ["arg1"], context: "MyContext");

        _registry.Steps[0].Context.ShouldBe("MyContext");
    }

    [Fact]
    public async Task RegisterCommand_ExecutesCommand_WithCorrectArgs()
    {
        var ops = CreateTestOps();

        ops.RegisterWithArgs("Test.Step", "testcmd", ["arg1", "arg2"]);
        await _registry.Steps[0].Execute();

        _executor.ExecutedCommands.ShouldHaveSingleItem();
        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("testcmd");
        cmd.HasArg("arg1").ShouldBeTrue();
        cmd.HasArg("arg2").ShouldBeTrue();
    }

    [Fact]
    public async Task RegisterCommand_WithWorkingDirectory_SetsCommandOptions()
    {
        var ops = CreateTestOps();

        ops.RegisterWithArgs("Test.Step", "testcmd", ["arg1"], workDir: "/custom/path");
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Options.ShouldNotBeNull();
        cmd.Options!.WorkingDirectory.ShouldBe("/custom/path");
    }

    [Fact]
    public async Task RegisterCommand_WithoutWorkingDirectory_HasNullWorkingDirectory()
    {
        var ops = CreateTestOps();

        ops.RegisterWithArgs("Test.Step", "testcmd", ["arg1"]);
        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Options.ShouldNotBeNull();
        cmd.Options!.WorkingDirectory.ShouldBeNull();
    }

    [Fact]
    public void RegisterCommand_WithBuilder_RegistersStep()
    {
        var ops = CreateTestOps();

        ops.RegisterWithBuilder("Test.Builder", "buildcmd",
            () => new ArgumentBuilder().Add("build").AddFlag(true, "--verbose"));

        _registry.Steps.ShouldHaveSingleItem();
        _registry.Steps[0].Name.ShouldBe("Test.Builder");
    }

    [Fact]
    public async Task RegisterCommand_WithBuilder_BuildsArgsCorrectly()
    {
        var ops = CreateTestOps();

        ops.RegisterWithBuilder("Test.Builder", "buildcmd",
            () => new ArgumentBuilder()
                .Add("build", "project.csproj")
                .AddIfNotNull("-c", "Release")
                .AddFlag(true, "--no-restore"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("buildcmd");
        cmd.HasArg("build").ShouldBeTrue();
        cmd.HasArg("project.csproj").ShouldBeTrue();
        cmd.HasArg("-c").ShouldBeTrue();
        cmd.HasArg("Release").ShouldBeTrue();
        cmd.HasArg("--no-restore").ShouldBeTrue();
    }

    [Fact]
    public async Task RegisterCommand_OnSuccess_ReturnsTrue()
    {
        var ops = CreateTestOps();

        ops.RegisterWithArgs("Test.Step", "testcmd", ["arg1"]);
        var result = await _registry.Steps[0].Execute();

        result.ShouldBeTrue();
    }

    [Fact]
    public async Task RegisterCommand_OnFailure_ReturnsFalse()
    {
        var ops = CreateTestOps();
        _executor.SimulateFailure = true;

        ops.RegisterWithArgs("Test.Step", "testcmd", ["arg1"]);
        var result = await _registry.Steps[0].Execute();

        result.ShouldBeFalse();
    }
}
