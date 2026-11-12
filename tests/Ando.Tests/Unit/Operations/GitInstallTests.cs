// =============================================================================
// GitInstallTests.cs
//
// Summary: Tests for Git.Install, which runs in the build container (not on the
// host). Registration is checked with MockExecutor; the embedded script is also
// run for real with fake apt-get/apk/git binaries on PATH.
// =============================================================================

using System.Diagnostics;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public sealed class GitInstallTests : IDisposable
{
    private readonly StepRegistry _registry = new();
    private readonly MockExecutor _executor = new();
    private readonly string _dir = Directory.CreateTempSubdirectory("ando-gitinst-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public async Task Install_RegistersContainerBashStep()
    {
        new GitOperations(_registry, new TestLogger(), () => _executor).Install();

        _registry.Steps.ShouldHaveSingleItem().Name.ShouldBe("Git.Install");
        await _registry.Steps[0].Execute();

        // Goes through the container executor factory, not the host git executor.
        _executor.LastCommand!.Command.ShouldBe("bash");
        _executor.LastCommand.Args.ShouldBe(["-c", GitOperations.InstallScript]);
    }

    [Fact]
    public void Script_UsesAptWithoutRecommendsOrApk()
    {
        GitOperations.InstallScript.ShouldContain("apt-get update -qq && apt-get install -y -qq --no-install-recommends git ca-certificates");
        GitOperations.InstallScript.ShouldContain("apk add --no-cache git");
        GitOperations.InstallScript.ShouldContain("unsupported image");
    }

    private void Fake(string name, string body)
    {
        // Fakes may need chmod; expose only that real tool.
        if (!File.Exists(Path.Combine(_dir, "chmod")))
        {
            File.CreateSymbolicLink(Path.Combine(_dir, "chmod"), File.Exists("/usr/bin/chmod") ? "/usr/bin/chmod" : "/bin/chmod");
        }
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, "#!/bin/sh\n" + body + "\n");
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
    }

    // Runs the script with only the fake tools (plus /bin for sh builtins' helpers) on PATH.
    private (int Exit, string Out, string Err, string Log) Run()
    {
        var log = Path.Combine(_dir, "calls.log");
        // Minimal PATH: the fake dir plus a dir with just the basics the script needs (none: all are builtins).
        var info = new ProcessStartInfo("/bin/bash") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(GitOperations.InstallScript);
        info.Environment["PATH"] = _dir;
        info.Environment["CALLS"] = log;
        using var p = Process.Start(info)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, o.Result, e.Result, File.Exists(log) ? File.ReadAllText(log) : "");
    }

    [Fact]
    public void Script_GitAlreadyPresent_SkipsInstall()
    {
        Fake("git", "echo 'git version 2.40.0'");
        Fake("apt-get", "echo apt-get >> \"$CALLS\"");

        var r = Run();

        r.Exit.ShouldBe(0, r.Err);
        r.Out.ShouldContain("git is already installed: git version 2.40.0");
        r.Log.ShouldBe("");
    }

    [Fact]
    public void Script_Apt_InstallsWithoutRecommendsAndLogsVersion()
    {
        Fake("apt-get", "echo \"apt-get $*\" >> \"$CALLS\"; printf '#!/bin/sh\\necho git version 9.9\\n' > \"${0%/*}/git\"; chmod +x \"${0%/*}/git\"");

        var r = Run();

        r.Exit.ShouldBe(0, r.Err);
        r.Log.ShouldContain("apt-get install -y -qq --no-install-recommends git ca-certificates");
        r.Out.ShouldContain("git version 9.9");
    }

    [Fact]
    public void Script_Apk_UsedWhenNoApt()
    {
        Fake("apk", "echo \"apk $*\" >> \"$CALLS\"; printf '#!/bin/sh\\necho git version 8.8\\n' > \"${0%/*}/git\"; chmod +x \"${0%/*}/git\"");

        var r = Run();

        r.Exit.ShouldBe(0, r.Err);
        r.Log.ShouldContain("apk add --no-cache git");
    }

    [Fact]
    public void Script_NoPackageManager_FailsWithUnsupportedImage()
    {
        var r = Run();

        r.Exit.ShouldBe(1);
        r.Err.ShouldContain("unsupported image");
    }
}
