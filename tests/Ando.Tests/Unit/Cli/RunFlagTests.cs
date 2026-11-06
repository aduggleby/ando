// =============================================================================
// RunFlagTests.cs
//
// Summary: Tests for the option check of `ando run` (AndoCli.FindUnknownRunFlag
// and FindRunFlagError): known options pass, typos and conflicts are rejected.
// =============================================================================

using Ando.Cli;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
public class RunFlagTests
{
    [Theory]
    [InlineData("run", "-p", "publish", "--dind", "--read-env")]
    [InlineData("run", "--dind", "--read-env", "-p", "x")]
    [InlineData("-f", "x.csando", "run", "--dind")]
    [InlineData("run", "positional")]
    [InlineData("run", "--no-env")]
    [InlineData("--no-env", "-p", "release")]
    public void FindUnknownRunFlag_AcceptedCommandLines_ReturnNull(params string[] args)
    {
        AndoCli.FindUnknownRunFlag(args).ShouldBeNull();
        AndoCli.FindRunFlagError(args).ShouldBeNull();
    }

    [Theory]
    [InlineData("--dry-rn", "run", "--dry-rn")]
    [InlineData("--help", "run", "--help")]
    [InlineData("-h", "run", "-h")]
    [InlineData("-v", "run", "-v")]
    [InlineData("--read_env", "run", "-p", "x", "--read_env")]
    public void FindUnknownRunFlag_RejectsUnknownOptions(string expected, params string[] args)
    {
        AndoCli.FindUnknownRunFlag(args).ShouldBe(expected);
        AndoCli.FindRunFlagError(args).ShouldBe($"Unknown option for run: {expected}");
    }

    [Fact]
    public void FindRunFlagError_NoEnvWithReadEnv_IsAnError()
    {
        AndoCli.FindUnknownRunFlag(["run", "--no-env", "--read-env"]).ShouldBeNull();
        AndoCli.FindRunFlagError(["run", "--no-env", "--read-env"])
            .ShouldBe("--no-env and --read-env cannot be used together.");
    }
}
