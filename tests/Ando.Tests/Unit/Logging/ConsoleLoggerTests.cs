using Ando.Logging;

namespace Ando.Tests.Unit.Logging;

/// <summary>
/// Tests for ConsoleLogger.
/// These tests must run sequentially due to Console.Out redirection.
/// </summary>
[Trait("Category", "Unit")]
[Collection("ConsoleOutput")]
public class ConsoleLoggerTests
{
    [Fact]
    public void Constructor_SetsDefaultVerbosity()
    {
        var logger = new ConsoleLogger();
        logger.Verbosity.ShouldBe(LogLevel.Normal);
    }

    [Fact]
    public void Verbosity_CanBeChanged()
    {
        var logger = new ConsoleLogger();
        logger.Verbosity = LogLevel.Detailed;
        logger.Verbosity.ShouldBe(LogLevel.Detailed);
    }

    [Fact]
    public void StepStarted_WritesToConsole()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.StepStarted("Test.Step", "Context");
        });

        output.ShouldContain("Test.Step");
        output.ShouldContain("Context");
    }

    [Fact]
    public void StepStarted_SuppressedInQuietMode()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Quiet;
            logger.StepStarted("Test.Step");
        });

        output.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void StepCompleted_WritesToConsole()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.StepCompleted("Test.Step", TimeSpan.FromSeconds(5));
        });

        output.ShouldContain("Test.Step");
    }

    [Fact]
    public void StepFailed_AlwaysWritesToConsole()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Quiet;
            logger.StepFailed("Test.Step", TimeSpan.FromSeconds(1), "Error message");
        });

        output.ShouldContain("Test.Step");
        output.ShouldContain("Error message");
    }

    [Fact]
    public void Error_AlwaysWritesToConsole()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Quiet;
            logger.Error("Critical error");
        });

        output.ShouldContain("Critical error");
    }

    [Fact]
    public void Debug_OnlyWritesInDetailedMode()
    {
        var normalOutput = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Normal;
            logger.Debug("Debug message");
        });

        var detailedOutput = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Detailed;
            logger.Debug("Debug message");
        });

        normalOutput.Trim().ShouldBeEmpty();
        detailedOutput.ShouldContain("Debug message");
    }

    [Fact]
    public void WorkflowCompleted_Success_ShowsMessage()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.WorkflowCompleted("ci", TimeSpan.FromMinutes(1), 5, 0);
        });

        output.ShouldContain("SUCCESS");
        output.ShouldContain("5 steps completed");
    }

    [Fact]
    public void WorkflowCompleted_Failure_ShowsMessage()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.WorkflowCompleted("ci", TimeSpan.FromMinutes(1), 5, 2);
        });

        output.ShouldContain("FAILED");
        output.ShouldContain("2/5 steps failed");
    }

    [Fact]
    public void Warning_WritesToConsoleInNormalMode()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Normal;
            logger.Warning("Warning message");
        });

        output.ShouldContain("Warning message");
    }

    [Fact]
    public void Info_WritesToConsoleInNormalMode()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Normal;
            logger.Info("Info message");
        });

        output.ShouldContain("Info message");
    }

    private static string CaptureOutput(Action action)
    {
        var originalOut = Console.Out;
        var writer = new StringWriter();
        try
        {
            Console.SetOut(writer);
            action();
            writer.Flush();
            return writer.ToString();
        }
        finally
        {
            Console.SetOut(originalOut);
            writer.Dispose();
        }
    }
}

/// <summary>
/// Collection definition for tests that manipulate console output.
/// </summary>
[CollectionDefinition("ConsoleOutput", DisableParallelization = true)]
public class ConsoleOutputCollection : ICollectionFixture<ConsoleOutputFixture>
{
}

public class ConsoleOutputFixture
{
}
