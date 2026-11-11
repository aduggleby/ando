// =============================================================================
// ArchiveAndFilesOperationsTests.cs
//
// Summary: Tests for Archive.Zip, Archive.TarGz and Files.Copy.
//
// Argument building is checked with MockExecutor. The embedded bash scripts are
// also run for real on the host (they only need bash, zip, tar and gzip) to
// verify determinism, symlink rejection, source immutability and the Files.Copy
// semantics. Tests that need a missing tool are skipped.
// =============================================================================

using System.Diagnostics;
using Ando.Operations;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public sealed class ArchiveAndFilesOperationsTests : IDisposable
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();
    private readonly string _tmp = Directory.CreateTempSubdirectory("ando-archive-").FullName;

    public void Dispose() => Directory.Delete(_tmp, recursive: true);

    private ArchiveOperations Archive() => new(_registry, _logger, () => _executor);
    private FilesOperations Files() => new(_registry, _logger, () => _executor);

    // ---- Argument building --------------------------------------------------

    [Fact]
    public async Task Zip_PassesResolvedPathsDefaultTimestampAndLevel()
    {
        Archive().Zip("artifacts/web", "artifacts/web.zip");

        await _registry.Steps[0].Execute();

        _registry.Steps[0].Name.ShouldBe("Archive.Zip");
        _registry.Steps[0].Context.ShouldBe("artifacts/web.zip");
        var cmd = _executor.LastCommand!;
        cmd.Command.ShouldBe("bash");
        cmd.Args.ShouldBe(["-c", ArchiveOperations.Script, "ando-archive",
            "/workspace/artifacts/web", "/workspace/artifacts/web.zip", "315532800", "6", "zip"]);
    }

    [Fact]
    public async Task TarGz_PassesTimestampAndLevelOptions()
    {
        Archive().TarGz("/out/api", "/out/api.tar.gz", o => o.WithTimestamp(1700000000).WithCompressionLevel(9));

        await _registry.Steps[0].Execute();

        _registry.Steps[0].Name.ShouldBe("Archive.TarGz");
        _executor.LastCommand!.Args[3..].ShouldBe(["/out/api", "/out/api.tar.gz", "1700000000", "9", "targz"]);
    }

    [Fact]
    public void Options_AreValidated()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => new ArchiveOptions().WithTimestamp(-1));
        Should.Throw<ArgumentOutOfRangeException>(() => new ArchiveOptions().WithCompressionLevel(0));
        Should.Throw<ArgumentOutOfRangeException>(() => Archive().Zip("a", "a.zip", o => o.WithTimestamp(1000)));
        Archive().TarGz("a", "a.tar.gz", o => o.WithTimestamp(0));
    }

    [Fact]
    public void Script_UsesTheDeterminismFlags()
    {
        var script = ArchiveOperations.Script;
        script.ShouldContain("zip -X");
        script.ShouldContain("--sort=name");
        script.ShouldContain("--owner=0 --group=0 --numeric-owner");
        script.ShouldContain("gzip -n");
        script.ShouldContain("LC_ALL=C TZ=UTC");
        script.ShouldContain("apt-get install");
    }

    [Fact]
    public async Task Copy_PassesResolvedPathsAndOptionalFlag()
    {
        Files().Copy("config/prod.json", "artifacts/web/", optional: true);

        await _registry.Steps[0].Execute();

        _registry.Steps[0].Name.ShouldBe("Files.Copy");
        _executor.LastCommand!.Args.ShouldBe(["-c", FilesOperations.CopyScript, "ando-files-copy",
            "/workspace/config/prod.json", "/workspace/artifacts/web/", "1"]);
    }

    // ---- Real script runs ---------------------------------------------------

    private static bool Has(string tool)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("bash", ["-c", $"command -v {tool}"])
            { RedirectStandardOutput = true, UseShellExecute = false })!;
            p.WaitForExit();
            return p.ExitCode == 0;
        }
        catch { return false; }
    }

    private (int Exit, string Out, string Err) Bash(string script, params string[] args)
    {
        var info = new ProcessStartInfo("bash")
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("-c");
        info.ArgumentList.Add(script);
        info.ArgumentList.Add("test");
        foreach (var a in args) info.ArgumentList.Add(a);
        using var p = Process.Start(info)!;
        var o = p.StandardOutput.ReadToEndAsync();
        var e = p.StandardError.ReadToEndAsync();
        p.WaitForExit();
        return (p.ExitCode, o.Result.Trim(), e.Result.Trim());
    }

    private string MakeTree(string name, bool reverse, DateTime mtime)
    {
        var root = Path.Combine(_tmp, name);
        Directory.CreateDirectory(root);
        void File(string rel, string text, bool exec = false)
        {
            var path = Path.Combine(root, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            System.IO.File.WriteAllText(path, text);
            System.IO.File.SetUnixFileMode(path, exec
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        string[] order = ["a/one.txt", "b/deep/two.txt", "z.sh", "a-b.txt"];
        foreach (var rel in reverse ? order.Reverse() : order)
        {
            File(rel, rel.Length + "-content", exec: rel == "z.sh");
        }
        Directory.CreateDirectory(Path.Combine(root, "empty"));
        foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
        {
            System.IO.File.SetLastWriteTimeUtc(entry, mtime);
        }
        return root;
    }

    [SkippableTheory]
    [InlineData("zip", "a.zip")]
    [InlineData("targz", "a.tar.gz")]
    public void Archive_SameTreeDifferentMtimesAndOrder_IsByteIdentical(string kind, string file)
    {
        Skip.IfNot(Has("zip") && Has("tar") && Has("gzip") && Has("sha256sum"), "zip, tar, gzip or sha256sum missing");
        var a = MakeTree("A", reverse: false, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        var b = MakeTree("B", reverse: true, new DateTime(2024, 11, 11, 11, 11, 10, DateTimeKind.Utc));

        var ra = Bash(ArchiveOperations.Script, a, Path.Combine(_tmp, "outA", file), "1700000000", "6", kind);
        var rb = Bash(ArchiveOperations.Script, b, Path.Combine(_tmp, "outB", file), "1700000000", "6", kind);

        ra.Exit.ShouldBe(0, ra.Err);
        rb.Exit.ShouldBe(0, rb.Err);
        ra.Out.ShouldBe(rb.Out);
        File.ReadAllBytes(Path.Combine(_tmp, "outA", file)).ShouldBe(File.ReadAllBytes(Path.Combine(_tmp, "outB", file)));
    }

    [SkippableFact]
    public void Archive_DoesNotTouchSourceTimestamps_AndRejectsSymlinks()
    {
        Skip.IfNot(Has("zip") && Has("sha256sum"), "zip or sha256sum missing");
        var src = MakeTree("S", reverse: false, new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc));
        var before = File.GetLastWriteTimeUtc(Path.Combine(src, "a/one.txt"));

        Bash(ArchiveOperations.Script, src, Path.Combine(_tmp, "out", "s.zip"), "315532800", "6", "zip").Exit.ShouldBe(0);
        File.GetLastWriteTimeUtc(Path.Combine(src, "a/one.txt")).ShouldBe(before);

        File.CreateSymbolicLink(Path.Combine(src, "link"), "z.sh");
        var rejected = Bash(ArchiveOperations.Script, src, Path.Combine(_tmp, "out", "t.zip"), "315532800", "6", "zip");
        rejected.Exit.ShouldNotBe(0);
        rejected.Err.ShouldContain("symlinks are not supported");
    }

    [Fact]
    public void FilesCopy_FileToFile_CreatesParentAndOverwrites()
    {
        File.WriteAllText(Path.Combine(_tmp, "in.txt"), "new");
        Directory.CreateDirectory(Path.Combine(_tmp, "o"));
        File.WriteAllText(Path.Combine(_tmp, "o", "out.txt"), "old");

        Bash(FilesOperations.CopyScript, Path.Combine(_tmp, "in.txt"), Path.Combine(_tmp, "o", "out.txt"), "0").Exit.ShouldBe(0);
        Bash(FilesOperations.CopyScript, Path.Combine(_tmp, "in.txt"), Path.Combine(_tmp, "deep/er/out.txt"), "0").Exit.ShouldBe(0);

        File.ReadAllText(Path.Combine(_tmp, "o", "out.txt")).ShouldBe("new");
        File.ReadAllText(Path.Combine(_tmp, "deep/er/out.txt")).ShouldBe("new");
    }

    [Fact]
    public void FilesCopy_FileToTrailingSlash_CopiesIntoDirectory()
    {
        File.WriteAllText(Path.Combine(_tmp, "in.txt"), "x");

        Bash(FilesOperations.CopyScript, Path.Combine(_tmp, "in.txt"), Path.Combine(_tmp, "dir") + "/", "0").Exit.ShouldBe(0);

        File.ReadAllText(Path.Combine(_tmp, "dir", "in.txt")).ShouldBe("x");
    }

    [Fact]
    public void FilesCopy_DirectoryContentsMergeIntoExistingDestination()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "src/sub"));
        File.WriteAllText(Path.Combine(_tmp, "src/a.txt"), "src-a");
        File.WriteAllText(Path.Combine(_tmp, "src/sub/b.txt"), "src-b");
        Directory.CreateDirectory(Path.Combine(_tmp, "dst"));
        File.WriteAllText(Path.Combine(_tmp, "dst/a.txt"), "old-a");
        File.WriteAllText(Path.Combine(_tmp, "dst/keep.txt"), "keep");

        Bash(FilesOperations.CopyScript, Path.Combine(_tmp, "src"), Path.Combine(_tmp, "dst"), "0").Exit.ShouldBe(0);

        File.ReadAllText(Path.Combine(_tmp, "dst/a.txt")).ShouldBe("src-a");
        File.ReadAllText(Path.Combine(_tmp, "dst/sub/b.txt")).ShouldBe("src-b");
        File.ReadAllText(Path.Combine(_tmp, "dst/keep.txt")).ShouldBe("keep");
        Directory.Exists(Path.Combine(_tmp, "dst/src")).ShouldBeFalse();
    }

    [Fact]
    public void FilesCopy_DirectoryIntoNewDestination_CreatesIt()
    {
        Directory.CreateDirectory(Path.Combine(_tmp, "src"));
        File.WriteAllText(Path.Combine(_tmp, "src/a.txt"), "a");

        Bash(FilesOperations.CopyScript, Path.Combine(_tmp, "src"), Path.Combine(_tmp, "x/y/dst"), "0").Exit.ShouldBe(0);

        File.ReadAllText(Path.Combine(_tmp, "x/y/dst/a.txt")).ShouldBe("a");
    }

    [Fact]
    public void FilesCopy_MissingSource_FailsUnlessOptional()
    {
        var missing = Path.Combine(_tmp, "nope");

        var failed = Bash(FilesOperations.CopyScript, missing, Path.Combine(_tmp, "d"), "0");
        failed.Exit.ShouldNotBe(0);
        failed.Err.ShouldContain("source not found");

        var skipped = Bash(FilesOperations.CopyScript, missing, Path.Combine(_tmp, "d"), "1");
        skipped.Exit.ShouldBe(0);
        skipped.Out.ShouldContain("skipped (optional)");
        Directory.Exists(Path.Combine(_tmp, "d")).ShouldBeFalse();
    }
}
