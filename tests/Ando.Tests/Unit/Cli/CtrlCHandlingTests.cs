using Ando.Cli;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
public class CtrlCHandlingTests : IDisposable
{
    // HandleCancelKeyPress turns on the process-wide Ctrl+C shield; reset it.
    public void Dispose() => Ando.Execution.CommandExecutorBase.ShieldNewProcessesFromCtrlC = false;

    [Fact]
    public void FirstCtrlC_RequestsGracefulCancellation()
    {
        using var cli = new AndoCli(new[] { "run" });

        var shouldForceExit = cli.HandleCancelKeyPress();

        shouldForceExit.ShouldBeFalse();
        cli.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void SecondCtrlC_DoesNotForceExit_SoCleanupCanFinish()
    {
        using var cli = new AndoCli(new[] { "run" });

        var firstPressShouldForceExit = cli.HandleCancelKeyPress();
        var secondPressShouldForceExit = cli.HandleCancelKeyPress();

        firstPressShouldForceExit.ShouldBeFalse();
        secondPressShouldForceExit.ShouldBeFalse();
        cli.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void ThirdCtrlC_RequestsImmediateExit()
    {
        using var cli = new AndoCli(new[] { "run" });

        var firstPressShouldForceExit = cli.HandleCancelKeyPress();
        var secondPressShouldForceExit = cli.HandleCancelKeyPress();
        var thirdPressShouldForceExit = cli.HandleCancelKeyPress();

        firstPressShouldForceExit.ShouldBeFalse();
        secondPressShouldForceExit.ShouldBeFalse();
        thirdPressShouldForceExit.ShouldBeTrue();
    }
}
