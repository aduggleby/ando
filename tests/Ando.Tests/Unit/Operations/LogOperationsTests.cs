// =============================================================================
// LogOperationsTests.cs
//
// Summary: Unit tests for LogOperations class.
//
// Tests verify that LogOperations registers log steps with the correct
// properties (IsLogStep, LogLevel, LogMessage).
// =============================================================================

using Ando.Operations;
using Ando.Steps;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class LogOperationsTests
{
    private readonly StepRegistry _registry = new();

    [Fact]
    public void Info_RegistersLogStep()
    {
        var log = new LogOperations(_registry);

        log.Info("Test info message");

        _registry.Steps.Count.ShouldBe(1);
        _registry.Steps[0].Name.ShouldBe("Log.Info");
        _registry.Steps[0].IsLogStep.ShouldBeTrue();
        _registry.Steps[0].LogLevel.ShouldBe(LogStepLevel.Info);
        _registry.Steps[0].LogMessage.ShouldBe("Test info message");
    }

    [Fact]
    public void Warning_RegistersLogStep()
    {
        var log = new LogOperations(_registry);

        log.Warning("Test warning message");

        _registry.Steps.Count.ShouldBe(1);
        _registry.Steps[0].Name.ShouldBe("Log.Warning");
        _registry.Steps[0].IsLogStep.ShouldBeTrue();
        _registry.Steps[0].LogLevel.ShouldBe(LogStepLevel.Warning);
        _registry.Steps[0].LogMessage.ShouldBe("Test warning message");
    }

    [Fact]
    public void Error_RegistersLogStep()
    {
        var log = new LogOperations(_registry);

        log.Error("Test error message");

        _registry.Steps.Count.ShouldBe(1);
        _registry.Steps[0].Name.ShouldBe("Log.Error");
        _registry.Steps[0].IsLogStep.ShouldBeTrue();
        _registry.Steps[0].LogLevel.ShouldBe(LogStepLevel.Error);
        _registry.Steps[0].LogMessage.ShouldBe("Test error message");
    }

    [Fact]
    public void Debug_RegistersLogStep()
    {
        var log = new LogOperations(_registry);

        log.Debug("Test debug message");

        _registry.Steps.Count.ShouldBe(1);
        _registry.Steps[0].Name.ShouldBe("Log.Debug");
        _registry.Steps[0].IsLogStep.ShouldBeTrue();
        _registry.Steps[0].LogLevel.ShouldBe(LogStepLevel.Debug);
        _registry.Steps[0].LogMessage.ShouldBe("Test debug message");
    }
}
