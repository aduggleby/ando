// =============================================================================
// ContainerExecutorDefaultEnvironmentTests.cs
//
// Summary: Tests that ContainerExecutor.DefaultEnvironment entries (for example
// the run's private AZURE_CONFIG_DIR) become `docker exec -e` arguments.
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Execution;

[Collection("Environment Variables")]
[Trait("Category", "Unit")]
public class ContainerExecutorDefaultEnvironmentTests
{
    private sealed class TestableContainerExecutor(string containerId, TestLogger logger)
        : ContainerExecutor(containerId, logger)
    {
        public ProcessStartInfo Prepare(string command, string[] args, CommandOptions options) =>
            PrepareProcessStartInfo(command, args, options);
    }

    private readonly TestLogger _logger = new();

    private static List<string> EnvArgs(ProcessStartInfo startInfo)
    {
        var args = startInfo.ArgumentList.ToList();
        return args.Where((_, i) => i > 0 && args[i - 1] == "-e").ToList();
    }

    [Fact]
    public void DefaultEnvironment_EntriesBecomeEnvFlags()
    {
        var executor = new TestableContainerExecutor("c1", _logger);
        executor.DefaultEnvironment["AZURE_CONFIG_DIR"] = "/tmp/ando-azure-abc";
        executor.DefaultEnvironment["OTHER"] = "value";

        var startInfo = executor.Prepare("az", ["account", "show"], new CommandOptions());

        var envArgs = EnvArgs(startInfo);
        envArgs.ShouldContain("AZURE_CONFIG_DIR=/tmp/ando-azure-abc");
        envArgs.ShouldContain("OTHER=value");
    }

    [Fact]
    public void DefaultEnvironment_PrecedesContainerIdAndCommand()
    {
        var executor = new TestableContainerExecutor("c1", _logger);
        executor.DefaultEnvironment["FOO"] = "bar";

        var args = executor.Prepare("az", ["version"], new CommandOptions()).ArgumentList.ToList();

        args.IndexOf("FOO=bar").ShouldBeLessThan(args.IndexOf("c1"));
        args.IndexOf("c1").ShouldBeLessThan(args.IndexOf("az"));
    }

    [Fact]
    public void PerCommandEnvironment_WinsOverDefault()
    {
        var executor = new TestableContainerExecutor("c1", _logger);
        executor.DefaultEnvironment["FOO"] = "default";
        var options = new CommandOptions();
        options.Environment["FOO"] = "command";

        var envArgs = EnvArgs(executor.Prepare("az", [], options));

        envArgs.ShouldContain("FOO=command");
        envArgs.ShouldNotContain("FOO=default");
    }

    [Fact]
    public void NoDefaultEnvironment_AddsNothingExtra()
    {
        var executor = new TestableContainerExecutor("c1", _logger);

        EnvArgs(executor.Prepare("az", [], new CommandOptions())).ShouldNotContain(a => a.StartsWith("AZURE_CONFIG_DIR="));
    }
}
