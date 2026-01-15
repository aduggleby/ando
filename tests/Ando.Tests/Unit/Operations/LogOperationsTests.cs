// =============================================================================
// LogOperationsTests.cs
//
// Summary: Unit tests for LogOperations class.
//
// Tests verify that LogOperations correctly delegates to the logger
// at each log level.
// =============================================================================

using Ando.Operations;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class LogOperationsTests
{
    private readonly TestLogger _logger = new();

    [Fact]
    public void Info_LogsMessage()
    {
        var log = new LogOperations(_logger);

        log.Info("Test info message");

        _logger.InfoMessages.ShouldContain("Test info message");
    }

    [Fact]
    public void Warning_LogsMessage()
    {
        var log = new LogOperations(_logger);

        log.Warning("Test warning message");

        _logger.WarningMessages.ShouldContain("Test warning message");
    }

    [Fact]
    public void Error_LogsMessage()
    {
        var log = new LogOperations(_logger);

        log.Error("Test error message");

        _logger.ErrorMessages.ShouldContain("Test error message");
    }

    [Fact]
    public void Debug_LogsMessage()
    {
        var log = new LogOperations(_logger);

        log.Debug("Test debug message");

        _logger.DebugMessages.ShouldContain("Test debug message");
    }
}
