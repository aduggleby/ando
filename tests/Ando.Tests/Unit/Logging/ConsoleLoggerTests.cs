// =============================================================================
// ConsoleLoggerTests.cs
//
// Summary: Unit tests for ConsoleLogger class.
//
// Tests verify logging output, verbosity levels, step status formatting,
// and workflow completion messages. Tests run sequentially due to
// Console.Out redirection.
// =============================================================================

using Ando.Logging;

namespace Ando.Tests.Unit.Logging;
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
            logger.WorkflowCompleted("ci", "/path/to/build.csando", TimeSpan.FromMinutes(1), 5, 0);
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
            logger.WorkflowCompleted("ci", "/path/to/build.csando", TimeSpan.FromMinutes(1), 5, 2);
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

    [Fact]
    public void StepSkipped_WritesToConsole()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.StepSkipped("Test.Step", "already up to date");
        });

        output.ShouldContain("Test.Step");
        output.ShouldContain("skipped");
        output.ShouldContain("already up to date");
    }

    [Fact]
    public void StepSkipped_WithoutReason_OmitsReason()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.StepSkipped("Test.Step");
        });

        output.ShouldContain("Test.Step");
        output.ShouldContain("skipped");
    }

    [Fact]
    public void WorkflowStarted_WritesToConsole()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.WorkflowStarted("ci", "build.csando", totalSteps: 5);
        });

        // Should write separator line
        output.ShouldContain("────");
    }

    [Fact]
    public void WorkflowStarted_SuppressedInQuietMode()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Quiet;
            logger.WorkflowStarted("ci", "build.csando", totalSteps: 5);
        });

        output.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void StepCompleted_SuppressedInQuietMode()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.Verbosity = LogLevel.Quiet;
            logger.StepCompleted("Test.Step", TimeSpan.FromSeconds(1));
        });

        output.Trim().ShouldBeEmpty();
    }

    [Fact]
    public void FormatDuration_SubMinute_FormatsAsSeconds()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.StepCompleted("Test", TimeSpan.FromSeconds(45.123));
        });

        output.ShouldContain("45.123s");
    }

    [Fact]
    public void FormatDuration_OverMinute_FormatsAsMinutesSeconds()
    {
        var output = CaptureOutput(() =>
        {
            var logger = new ConsoleLogger(useColor: false);
            logger.StepCompleted("Test", TimeSpan.FromMinutes(2) + TimeSpan.FromSeconds(30));
        });

        output.ShouldContain("02:30");
    }

    [Fact]
    public void Dispose_CanBeCalledTwice()
    {
        var logger = new ConsoleLogger(useColor: false);

        // Should not throw
        logger.Dispose();
        logger.Dispose();
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
