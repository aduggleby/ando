using Ando.Cli;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
public class CtrlCHandlingTests
{
    [Fact]
    public void FirstCtrlC_RequestsGracefulCancellation()
    {
        using var cli = new AndoCli(new[] { "run" });

        var shouldForceExit = cli.HandleCancelKeyPress();

        shouldForceExit.ShouldBeFalse();
        cli.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public void SecondCtrlC_RequestsImmediateExit()
    {
        using var cli = new AndoCli(new[] { "run" });

        var firstPressShouldForceExit = cli.HandleCancelKeyPress();
        var secondPressShouldForceExit = cli.HandleCancelKeyPress();

        firstPressShouldForceExit.ShouldBeFalse();
        secondPressShouldForceExit.ShouldBeTrue();
    }
}
