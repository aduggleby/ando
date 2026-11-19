// =============================================================================
// RunLockTests.cs
//
// Summary: Tests for RunLockSet (container lock and Ando.Lock names): acquire,
// held by another process, release, ANDO_HELD_LOCKS, plus Ando.Lock during
// script evaluation (no step, dry run, exit-code-9 exception).
// The two-process tests use the `flock` and `sleep` tools and run on Linux only.
// =============================================================================

using System.Diagnostics;
using Ando.Execution;
using Ando.Scripting;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Execution;

[Trait("Category", "Unit")]
public class RunLockTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ando-locks-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private RunLockSet NewSet(string inherited = "") => new(_dir, inherited);

    [Fact]
    public void AcquireContainer_SecondHolderFailsNamingTheContainer()
    {
        using var first = NewSet();
        using var second = NewSet();
        first.AcquireContainer("ando-site-abc12345");

        var ex = Assert.Throws<RunLockHeldException>(() => second.AcquireContainer("ando-site-abc12345"));
        Assert.Contains("ando-site-abc12345", ex.Message);
        Assert.Contains("container-ando-site-abc12345.lock", ex.Message);
    }

    [Fact]
    public void AcquireContainer_DifferentContainersDoNotConflict()
    {
        using var first = NewSet();
        using var second = NewSet();
        first.AcquireContainer("ando-a-1");
        second.AcquireContainer("ando-b-1");
    }

    [Fact]
    public void Dispose_ReleasesTheLock()
    {
        var first = NewSet();
        first.AcquireContainer("ando-a-1");
        first.Dispose();

        using var second = NewSet();
        second.AcquireContainer("ando-a-1");
    }

    [Fact]
    public void AcquireContainer_UncreatableDirectoryWarnsAndContinues()
    {
        var blocker = Path.Combine(_dir, "file");
        File.WriteAllText(blocker, "x");
        using var set = new RunLockSet(Path.Combine(blocker, "locks"), "");

        set.AcquireContainer("ando-a-1");

        Assert.NotNull(set.LastWarning);
    }

    [Fact]
    public void AcquireNamed_HeldByAnotherSetFails()
    {
        using var first = NewSet();
        using var second = NewSet();
        Assert.True(first.AcquireNamed("ssg-release"));

        var ex = Assert.Throws<RunLockHeldException>(() => second.AcquireNamed("ssg-release"));
        Assert.Contains("ssg-release", ex.Message);
    }

    [Fact]
    public void AcquireNamed_SameNameTwiceIsNoOp()
    {
        using var set = NewSet();

        Assert.True(set.AcquireNamed("release"));
        Assert.False(set.AcquireNamed("release"));
        Assert.Equal(["release"], set.NamedLocks);
    }

    [Fact]
    public void AcquireNamed_InheritedNameIsSkipped()
    {
        using var parent = NewSet();
        parent.AcquireNamed("release");
        using var child = NewSet(parent.HeldForChildren);

        Assert.False(child.AcquireNamed("release"));
        Assert.Empty(child.NamedLocks);
        // A grandchild still sees the name.
        Assert.Contains("release", child.HeldForChildren.Split('\n'));
    }

    [Fact]
    public void HeldForChildren_ListsInheritedAndOwnNames()
    {
        using var set = NewSet("a\nb");
        set.AcquireNamed("c");

        Assert.Equal(["a", "b", "c"], set.HeldForChildren.Split('\n'));
    }

    [Fact]
    public void AcquireNamed_NamesSanitizingToTheSameFileAreOneLock()
    {
        using var set = NewSet();

        Assert.True(set.AcquireNamed("a/b"));
        Assert.False(set.AcquireNamed("a_b"));
        Assert.Equal(["a/b"], set.NamedLocks);
        Assert.Single(Directory.GetFiles(_dir, "name-a_b.lock"));
    }

    [Fact]
    public void ContainerAndNamedLocksUseDifferentFiles()
    {
        using var set = NewSet();
        set.AcquireContainer("x");
        set.AcquireNamed("x");

        Assert.Equal(["container-x.lock", "name-x.lock"],
            Directory.GetFiles(_dir).Select(Path.GetFileName).Order().ToArray());
    }

    [SkippableFact]
    public void UnwritableLockFolder_WarnsForContainerLock_AndFailsNamedLockClearly()
    {
        Skip.If(OperatingSystem.IsWindows() || Environment.UserName == "root", "needs permission checks (not root, not Windows)");
        var locked = Path.Combine(_dir, "ro");
        Directory.CreateDirectory(locked);
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            using var set = new RunLockSet(locked, "");

            set.AcquireContainer("c1");
            Assert.NotNull(set.LastWarning);

            var ex = Assert.Throws<InvalidOperationException>(() => set.AcquireNamed("n1"));
            Assert.IsNotType<RunLockHeldException>(ex);
            Assert.Contains("n1", ex.Message);
        }
        finally
        {
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    [Fact]
    public void AcquireNamed_NamesWithSlashesStayInsideTheLockFolder()
    {
        using var set = NewSet();
        set.AcquireNamed("../evil/name");

        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_dir)!, "evil*"));
        Assert.Single(Directory.GetFiles(_dir, "*.lock"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("a\nb")]
    public void AcquireNamed_InvalidNameThrows(string name)
    {
        using var set = NewSet();
        Assert.Throws<ArgumentException>(() => set.AcquireNamed(name));
    }

    // Two processes: `flock` is a separate process that tries the same file.

    private static bool FlockAvailable => OperatingSystem.IsLinux() && File.Exists("/usr/bin/flock");

    private static int RunFlock(params string[] args)
    {
        var info = new ProcessStartInfo("flock") { UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = Process.Start(info)!;
        process.WaitForExit();
        return process.ExitCode;
    }

    [SkippableFact]
    public void Lock_IsHeldAgainstAnotherProcessUntilDisposed()
    {
        Skip.IfNot(FlockAvailable, "needs Linux with /usr/bin/flock");

        var set = NewSet();
        set.AcquireNamed("two-process");
        var file = Path.Combine(_dir, "name-two-process.lock");

        Assert.Equal(1, RunFlock("-n", file, "true"));
        set.Dispose();
        Assert.Equal(0, RunFlock("-n", file, "true"));
    }

    [SkippableFact]
    public void Lock_HeldByAnotherProcessFailsAcquire()
    {
        Skip.IfNot(FlockAvailable, "needs Linux with /usr/bin/flock");

        var file = Path.Combine(_dir, "name-other-process.lock");
        var info = new ProcessStartInfo("flock") { UseShellExecute = false };
        foreach (var arg in new[] { file, "sleep", "30" }) info.ArgumentList.Add(arg);
        using var holder = Process.Start(info)!;
        try
        {
            // Wait until the holder has the lock: poll until `flock -n` can no longer take it.
            var deadline = DateTime.UtcNow.AddSeconds(5);
            var held = false;
            while (!held && DateTime.UtcNow < deadline)
            {
                held = File.Exists(file) && RunFlock("-n", file, "true") != 0;
                if (!held) Thread.Sleep(20);
            }
            Assert.True(held, "the flock holder never took the lock");

            using var set = NewSet();
            Assert.Throws<RunLockHeldException>(() => set.AcquireNamed("other-process"));
        }
        finally
        {
            holder.Kill(entireProcessTree: true);
            holder.WaitForExit();
        }
    }

    [SkippableFact]
    public void Lock_IsNotInheritedByChildProcesses()
    {
        Skip.IfNot(FlockAvailable, "needs Linux with /usr/bin/flock");

        var set = NewSet();
        set.AcquireNamed("cloexec");
        // A long-running child (like docker or git) started while the lock is held.
        using var child = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false })!;
        try
        {
            set.Dispose();
            // The lock is free although the child still runs: the child did not inherit it.
            Assert.Equal(0, RunFlock("-n", Path.Combine(_dir, "name-cloexec.lock"), "true"));
        }
        finally
        {
            child.Kill();
            child.WaitForExit();
        }
    }

    // Ando.Lock during script evaluation.

    private async Task<(BuildContext Context, TestLogger Logger)> LoadAsync(
        string script, RunLockSet locks, bool dryRun = false)
    {
        var logger = new TestLogger();
        var scriptPath = Path.Combine(_dir, "build.csando");
        await File.WriteAllTextAsync(scriptPath, script);
        var host = new ScriptHost(logger)
        {
            ConfigureContext = ctx =>
            {
                ctx.Options.Session.Locks = locks;
                ctx.Options.Session.DryRun = dryRun;
            }
        };
        return (await host.LoadScriptAsync(scriptPath, _dir), logger);
    }

    [Fact]
    public async Task AndoLock_TakesLockAtEvaluation_IsNotAStep_AndIsRecorded()
    {
        using var locks = NewSet();
        var (context, _) = await LoadAsync("Ando.Lock(\"release\");\nAndo.Lock(\"release\");", locks);

        Assert.Empty(context.StepRegistry.Steps);
        Assert.Equal(["release"], locks.NamedLocks);
        var reports = context.Options.Session.Reports.Where(r => r.Key == "lock release").ToList();
        Assert.Single(reports);

        using var other = NewSet();
        Assert.Throws<RunLockHeldException>(() => other.AcquireNamed("release"));
    }

    [Fact]
    public async Task AndoLock_HeldByAnotherRun_ThrowsRunLockHeldException()
    {
        using var holder = NewSet();
        holder.AcquireNamed("release");
        using var locks = NewSet();

        // Roslyn must not wrap the exception: the CLI maps exactly this type to exit code 9.
        await Assert.ThrowsAsync<RunLockHeldException>(() => LoadAsync("Ando.Lock(\"release\");", locks));
    }

    [Fact]
    public async Task AndoLock_DryRunTakesNoLock()
    {
        using var holder = NewSet();
        holder.AcquireNamed("release");
        using var locks = NewSet();

        var (context, _) = await LoadAsync("Ando.Lock(\"release\");", locks, dryRun: true);

        Assert.Empty(locks.NamedLocks);
        Assert.Empty(context.Options.Session.Reports);
    }

    [Fact]
    public async Task AndoLock_NameHeldByParentIsSkipped()
    {
        using var parent = NewSet();
        parent.AcquireNamed("release");
        using var child = NewSet(parent.HeldForChildren);

        var (context, _) = await LoadAsync("Ando.Lock(\"release\");", child);

        Assert.Empty(child.NamedLocks);
        Assert.Empty(context.Options.Session.Reports);
    }
}
