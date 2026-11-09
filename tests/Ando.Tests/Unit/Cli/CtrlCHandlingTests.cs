using Ando.Execution;
using Ando.Tests.TestFixtures;
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

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(1, 0, 1)]
    [InlineData(5, 0, 5)]
    [InlineData(1, 1, 130)]
    [InlineData(1, 3, 130)]
    [InlineData(0, 1, 0)]
    public void ExitCodeAfterRun_Is130WhenCtrlCInterruptedTheRun(int runExitCode, int presses, int expected)
    {
        AndoCli.ExitCodeAfterRun(runExitCode, presses).ShouldBe(expected);
    }
}

[Trait("Category", "Unit")]
public class ColdContainerCleanupTests
{
    private sealed class RecordingDockerManager() : DockerManager(new TestLogger())
    {
        public List<string> Removed { get; } = [];

        public override Task RemoveContainerAsync(string containerName)
        {
            Removed.Add(containerName);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ColdRun_RemovesItsContainerOnce()
    {
        using var cli = new AndoCli(new[] { "run", "--cold" });
        var docker = new RecordingDockerManager();
        cli.RegisterColdContainer(docker, "ando-cold-test");

        (await cli.RemoveColdContainerAsync()).ShouldBeTrue();
        (await cli.RemoveColdContainerAsync()).ShouldBeFalse();

        docker.Removed.ShouldBe(["ando-cold-test"]);
    }

    [Fact]
    public async Task WarmRun_RemovesNothing()
    {
        using var cli = new AndoCli(new[] { "run" });

        (await cli.RemoveColdContainerAsync()).ShouldBeFalse();
    }
}
