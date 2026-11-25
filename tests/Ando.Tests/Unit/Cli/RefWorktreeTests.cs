// =============================================================================
// RefWorktreeTests.cs
//
// Summary: Tests for the --ref worktree (creation in the scratch folder, git's
// stderr on failure, the stale sweep) and the HostScratch folder rules, against
// a real temporary git repository.
// =============================================================================

using System.Diagnostics;
using Ando.Cli;
using Ando.Execution;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Cli;

[Trait("Category", "Unit")]
public class RefWorktreeTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ando-reftest-").FullName;
    private readonly string _repo;
    private readonly string _scratch;
    private readonly TestLogger _logger = new();

    public RefWorktreeTests()
    {
        _repo = Path.Combine(_root, "repo");
        _scratch = Path.Combine(_root, "scratch");
        Directory.CreateDirectory(_repo);
        Git("init", "-q");
        File.WriteAllText(Path.Combine(_repo, "build.csando"), "// build");
        Git("add", "-A");
        Git("-c", "user.email=t@example.com", "-c", "user.name=t", "commit", "-q", "-m", "first");
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string Script => Path.Combine(_repo, "build.csando");

    private string Git(params string[] args)
    {
        var psi = new ProcessStartInfo("git") { WorkingDirectory = _repo, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var output = p.StandardOutput.ReadToEnd();
        p.WaitForExit();
        p.ExitCode.ShouldBe(0, p.StandardError.ReadToEnd());
        return output;
    }

    [Fact]
    public async Task Create_PutsWorktreeAndLockInScratchFolder_AndDisposeRemovesBoth()
    {
        var worktree = await RefWorktree.CreateAsync(Script, "HEAD", _logger, _scratch, RefWorktree.DirectoryName);

        Path.GetDirectoryName(worktree.Path).ShouldBe(_scratch);
        File.Exists(worktree.ScriptPath).ShouldBeTrue();
        File.Exists(worktree.Path + ".lock").ShouldBeTrue();

        await worktree.DisposeAsync();

        Directory.Exists(worktree.Path).ShouldBeFalse();
        File.Exists(worktree.Path + ".lock").ShouldBeFalse();
        Git("worktree", "list").ShouldNotContain(worktree.Path);
    }

    [Fact]
    public async Task Create_WhenGitFails_IncludesGitStderr_AndKeepsExistingDirectory()
    {
        var name = "ando-ref-taken";
        var taken = Path.Combine(_scratch, name);
        Directory.CreateDirectory(taken);
        File.WriteAllText(Path.Combine(taken, "keep.txt"), "x");

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => RefWorktree.CreateAsync(Script, "HEAD", _logger, _scratch, _ => name));

        ex.Message.ShouldContain($"could not create a worktree at {taken}");
        ex.Message.ShouldContain("already exists");
        ex.Message.ShouldContain("ANDO_TMPDIR");
        File.Exists(Path.Combine(taken, "keep.txt")).ShouldBeTrue();
        File.Exists(taken + ".lock").ShouldBeFalse();
    }

    [Fact]
    public async Task Sweep_RemovesUnlockedEntries_KeepsHeldOnes_AndIgnoresEntriesWithoutLock()
    {
        var stale = await RefWorktree.CreateAsync(Script, "HEAD", _logger, _scratch, RefWorktree.DirectoryName);
        var live = await RefWorktree.CreateAsync(Script, "HEAD", _logger, _scratch, RefWorktree.DirectoryName);
        var foreign = Path.Combine(_scratch, "ando-ref-foreign");
        Directory.CreateDirectory(foreign);

        // Simulate a killed run: release the stale entry's lock without removing it.
        await ReleaseLockOnly(stale);

        await RefWorktree.SweepStaleAsync(_scratch, _repo, _logger);

        Directory.Exists(stale.Path).ShouldBeFalse();
        File.Exists(stale.Path + ".lock").ShouldBeFalse();
        Git("worktree", "list").ShouldNotContain(stale.Path);
        Directory.Exists(live.Path).ShouldBeTrue();
        Directory.Exists(foreign).ShouldBeTrue();

        await live.DisposeAsync();
    }

    [Fact]
    public async Task Sweep_NeverTouchesSystemTemp()
    {
        // A dead-looking entry in the system temp folder must survive.
        var name = $"ando-ref-sweeptest-{Guid.NewGuid():N}";
        var dir = Path.Combine(Path.GetTempPath(), name);
        Directory.CreateDirectory(dir);
        File.WriteAllText(dir + ".lock", "");
        try
        {
            await RefWorktree.SweepStaleAsync(Path.GetTempPath(), _repo, _logger);
            Directory.Exists(dir).ShouldBeTrue();
        }
        finally
        {
            Directory.Delete(dir);
            File.Delete(dir + ".lock");
        }
    }

    [Theory]
    [InlineData("/data/ando-scratch", "/xdg", "/home/u", "/data/ando-scratch")]
    [InlineData(null, "/xdg", "/home/u", "/xdg/ando/tmp")]
    [InlineData(null, null, "/home/u", "/home/u/.cache/ando/tmp")]
    [InlineData("  ", "relative", "/home/u", "/home/u/.cache/ando/tmp")]
    [InlineData(null, null, "", null)]
    [InlineData(null, null, "relative", null)]
    public void HostScratch_Resolve(string? env, string? xdg, string? home, string? expected)
    {
        if (OperatingSystem.IsWindows()) return;
        HostScratch.Resolve(env, xdg, home).ShouldBe(expected);
    }

    [Fact]
    public void HostScratch_CheckUsage_WarnsWhenFolderIsTooBig()
    {
        File.WriteAllBytes(Path.Combine(_root, "big.bin"), new byte[4096]);

        HostScratch.CheckUsage(_root, maxBytes: 1024, minFreeBytes: 0)!.ShouldContain("holds");
        HostScratch.CheckUsage(_root, maxBytes: long.MaxValue, minFreeBytes: 0).ShouldBeNull();
    }

    [Fact]
    public void HostScratch_CheckUsage_WarnsWhenDiskIsLow()
    {
        HostScratch.CheckUsage(_root, maxBytes: long.MaxValue, minFreeBytes: long.MaxValue)!.ShouldContain("free on the disk");
    }

    [Fact]
    public void HostScratch_CheckUsage_MissingFolder_ReturnsNull()
    {
        HostScratch.CheckUsage(Path.Combine(_root, "missing")).ShouldBeNull();
    }

    [Fact]
    public void HostScratch_IsSystemTemp_IgnoresTrailingSeparator()
    {
        HostScratch.IsSystemTemp(Path.GetTempPath()).ShouldBeTrue();
        HostScratch.IsSystemTemp(Path.TrimEndingDirectorySeparator(Path.GetTempPath())).ShouldBeTrue();
        HostScratch.IsSystemTemp(_scratch).ShouldBeFalse();
    }

    [Fact]
    public void FreeSpace_ReportsGigabytes()
    {
        RefWorktree.FreeSpace(_root)!.ShouldEndWith(" GB");
    }

    // Disposes only the worktree's lock stream, as if the process had died.
    private static Task ReleaseLockOnly(RefWorktree worktree)
    {
        var field = typeof(RefWorktree).GetField("_lock", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        ((FileStream?)field.GetValue(worktree))?.Dispose();
        return Task.CompletedTask;
    }
}
