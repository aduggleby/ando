// =============================================================================
// ArtifactManifestTests.cs
//
// Summary: Tests for ArtifactManifest (listing parsing, directory verification,
// file lookup) and Ando.WriteManifest / Ando.ImportArtifacts.
// =============================================================================

using System.Security.Cryptography;
using System.Text;
using Ando.Execution;
using Ando.Operations;
using Ando.Profiles;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class ArtifactManifestTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("ando-manifest-").FullName;
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();
    private readonly BuildOptions _options = new();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static string Sha(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    // Writes files into a subdirectory of the temp dir and returns a matching manifest.
    private ArtifactManifest WriteArtifacts(string name, Dictionary<string, string> files, bool writeManifest = true)
    {
        var root = Path.Combine(_dir, name);
        foreach (var (path, content) in files)
        {
            var full = Path.Combine(root, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, content);
        }

        var manifest = new ArtifactManifest
        {
            Version = "1.2.3",
            Commit = "abc123",
            Files = [.. files.Select(f => new ArtifactFile(f.Key, Encoding.UTF8.GetByteCount(f.Value), Sha(f.Value)))]
        };
        if (writeManifest)
        {
            File.WriteAllText(Path.Combine(root, ArtifactManifest.FileName), manifest.ToJson());
        }
        return manifest;
    }

    private AndoOperations _ops => _opsInstance ??= CreateAndo();
    private AndoOperations? _opsInstance;

    private AndoOperations CreateAndo() =>
        new(_registry, _logger, p => p, _options, new ArtifactOperations(_logger), new ProfileRegistry(),
            () => _executor);

    // ---- ParseListings ------------------------------------------------------

    [Fact]
    public void ParseListings_CombinesHashesAndSizes_SortedByPath()
    {
        var hashA = new string('a', 64);
        var hashB = new string('b', 64);
        var sha = $"{hashB}  ./web/index.html\n{hashA}  ./worker.tar.gz\n";
        var sizes = "120 ./web/index.html\n4096 ./worker.tar.gz\n";

        var files = ArtifactManifest.ParseListings(sha, sizes);

        files.ShouldBe(
        [
            new ArtifactFile("web/index.html", 120, hashB),
            new ArtifactFile("worker.tar.gz", 4096, hashA)
        ]);
    }

    [Fact]
    public void ParseListings_PathsWithSpacesAndCarriageReturns()
    {
        var hash = new string('c', 64);

        var files = ArtifactManifest.ParseListings($"{hash}  ./my file.txt\r\n", "7 ./my file.txt\r\n");

        files.ShouldBe([new ArtifactFile("my file.txt", 7, hash)]);
    }

    [Fact]
    public void ParseListings_MissingSizeOrShortLines_AreHandled()
    {
        var hash = new string('d', 64);

        var files = ArtifactManifest.ParseListings($"{hash}  ./a\nshort\n", "");

        files.ShouldBe([new ArtifactFile("a", 0, hash)]);
    }

    [Fact]
    public void ParseListings_Empty_ReturnsEmpty()
    {
        ArtifactManifest.ParseListings("", "").ShouldBeEmpty();
    }

    // ---- JSON round trip ----------------------------------------------------

    [Fact]
    public void Json_RoundTrips()
    {
        var manifest = WriteArtifacts("rt", new() { ["a.txt"] = "A" }, writeManifest: false);

        var parsed = ArtifactManifest.Parse(manifest.ToJson());

        parsed.Version.ShouldBe("1.2.3");
        parsed.Commit.ShouldBe("abc123");
        parsed.Files.ShouldBe(manifest.Files);
        manifest.ToJson().ShouldContain("\"sha256\"");
    }

    // ---- VerifyDirectory ----------------------------------------------------

    [Fact]
    public void VerifyDirectory_Match_ReturnsNoProblems_AndIgnoresManifestFile()
    {
        var manifest = WriteArtifacts("ok", new() { ["a.txt"] = "A", ["sub/b.txt"] = "B" });

        manifest.VerifyDirectory(Path.Combine(_dir, "ok")).ShouldBeEmpty();
    }

    [Fact]
    public void VerifyDirectory_DetectsMissingFile()
    {
        var manifest = WriteArtifacts("missing", new() { ["a.txt"] = "A", ["b.txt"] = "B" });
        File.Delete(Path.Combine(_dir, "missing", "b.txt"));

        manifest.VerifyDirectory(Path.Combine(_dir, "missing")).ShouldBe(["missing: b.txt"]);
    }

    [Fact]
    public void VerifyDirectory_DetectsChangedFile()
    {
        var manifest = WriteArtifacts("changed", new() { ["a.txt"] = "A" });
        File.WriteAllText(Path.Combine(_dir, "changed", "a.txt"), "tampered");

        manifest.VerifyDirectory(Path.Combine(_dir, "changed")).ShouldBe(["changed: a.txt"]);
    }

    [Fact]
    public void VerifyDirectory_DetectsExtraFile()
    {
        var manifest = WriteArtifacts("extra", new() { ["a.txt"] = "A" });
        File.WriteAllText(Path.Combine(_dir, "extra", "unexpected.txt"), "x");

        manifest.VerifyDirectory(Path.Combine(_dir, "extra")).ShouldBe(["not in manifest: unexpected.txt"]);
    }

    // ---- File lookup --------------------------------------------------------

    private static ArtifactManifest Sample() => new()
    {
        Files =
        [
            new ArtifactFile("documents/worker.tar.gz", 1, "h1"),
            new ArtifactFile("web/index.html", 2, "h2"),
            new ArtifactFile("web/app.js", 3, "h3"),
            new ArtifactFile("other/app.js", 4, "h4")
        ]
    };

    [Fact]
    public void File_ByRelativePath()
    {
        Sample().File("documents/worker.tar.gz").Sha256.ShouldBe("h1");
        Sample().File("./web/app.js").Sha256.ShouldBe("h3");
    }

    [Fact]
    public void File_ByUniqueName()
    {
        Sample().File("worker.tar.gz").Path.ShouldBe("documents/worker.tar.gz");
        Sample().File("index.html").Path.ShouldBe("web/index.html");
    }

    [Fact]
    public void File_AmbiguousName_Throws()
    {
        var ex = Should.Throw<InvalidOperationException>(() => Sample().File("app.js"));

        ex.Message.ShouldContain("2 files");
    }

    [Fact]
    public void File_Unknown_Throws()
    {
        Should.Throw<KeyNotFoundException>(() => Sample().File("nope.txt"));
    }

    [Fact]
    public void PathOf_CombinesContainerPathAndFilePath()
    {
        var manifest = Sample();
        manifest.ContainerPath = "/workspace/artifacts/";

        manifest.PathOf("worker.tar.gz").ShouldBe("/workspace/artifacts/documents/worker.tar.gz");
    }

    [Fact]
    public void PathOf_Directory_ReturnsContainerDirectory()
    {
        var manifest = Sample();
        manifest.ContainerPath = "/workspace/artifacts/";

        manifest.PathOf("documents").ShouldBe("/workspace/artifacts/documents");
        manifest.PathOf("./documents/").ShouldBe("/workspace/artifacts/documents");
    }

    [Fact]
    public void PathOf_NothingMatches_Throws()
    {
        var manifest = Sample();
        manifest.ContainerPath = "/workspace/artifacts";

        Should.Throw<KeyNotFoundException>(() => manifest.PathOf("docs"));
        Should.Throw<KeyNotFoundException>(() => manifest.PathOf("documents/missing"));
    }

    [Fact]
    public void File_Directory_StillThrows()
    {
        Should.Throw<KeyNotFoundException>(() => Sample().File("documents"));
    }

    // ---- Ando.ImportArtifacts ----------------------------------------------

    [Fact]
    public void ImportArtifacts_Match_ReturnsManifestAndRegistersStep()
    {
        WriteArtifacts("artifacts", new() { ["web/index.html"] = "<html/>", ["worker.tar.gz"] = "bytes" });
        _options.Session.InvocationRoot = _dir;

        var manifest = CreateAndo().ImportArtifacts("artifacts");

        manifest.Version.ShouldBe("1.2.3");
        manifest.Commit.ShouldBe("abc123");
        manifest.Files.Count.ShouldBe(2);
        manifest.ContainerPath.ShouldBe("/workspace/artifacts");
        manifest.PathOf("worker.tar.gz").ShouldBe("/workspace/artifacts/worker.tar.gz");
        _registry.Steps.ShouldHaveSingleItem().Name.ShouldBe("Ando.ImportArtifacts");
    }

    [Fact]
    public void ImportArtifacts_ExplicitContainerDirectory()
    {
        WriteArtifacts("artifacts", new() { ["a.txt"] = "A" });
        _options.Session.InvocationRoot = _dir;

        var manifest = CreateAndo().ImportArtifacts("artifacts", "/opt/deploy");

        manifest.ContainerPath.ShouldBe("/opt/deploy");
    }

    [Fact]
    public void ImportArtifacts_ChangedFile_ThrowsAtRegistration()
    {
        WriteArtifacts("artifacts", new() { ["a.txt"] = "A" });
        File.WriteAllText(Path.Combine(_dir, "artifacts", "a.txt"), "tampered");
        _options.Session.InvocationRoot = _dir;

        var ex = Should.Throw<InvalidOperationException>(() => CreateAndo().ImportArtifacts("artifacts"));

        ex.Message.ShouldContain("changed: a.txt");
        _registry.Steps.ShouldBeEmpty();
    }

    [Fact]
    public void ImportArtifacts_MissingAndExtraFiles_ThrowAtRegistration()
    {
        WriteArtifacts("artifacts", new() { ["a.txt"] = "A", ["b.txt"] = "B" });
        File.Delete(Path.Combine(_dir, "artifacts", "b.txt"));
        File.WriteAllText(Path.Combine(_dir, "artifacts", "c.txt"), "C");
        _options.Session.InvocationRoot = _dir;

        var ex = Should.Throw<InvalidOperationException>(() => CreateAndo().ImportArtifacts("artifacts"));

        ex.Message.ShouldContain("missing: b.txt");
        ex.Message.ShouldContain("not in manifest: c.txt");
    }

    [Fact]
    public void ImportArtifacts_NoManifest_Throws()
    {
        WriteArtifacts("artifacts", new() { ["a.txt"] = "A" }, writeManifest: false);
        _options.Session.InvocationRoot = _dir;

        var ex = Should.Throw<InvalidOperationException>(() => CreateAndo().ImportArtifacts("artifacts"));

        ex.Message.ShouldContain("manifest.json");
    }

    [Fact]
    public async Task ImportArtifacts_Step_FailsWithoutBuildContainer()
    {
        WriteArtifacts("artifacts", new() { ["a.txt"] = "A" });
        _options.Session.InvocationRoot = _dir;
        CreateAndo().ImportArtifacts("artifacts");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(_options);

        result.Success.ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("No build container"));
    }

    // ---- Ando.WriteManifest -------------------------------------------------

    // Replaces docker cp: records the arguments and the content of the copied host file.
    private List<(string[] Args, string Content)> CaptureDockerCopies()
    {
        var copies = new List<(string[] Args, string Content)>();
        _options.Session.ContainerId = "container-1";
        _options.Session.DockerRunner = args =>
        {
            copies.Add((args, args[0] == "cp" && File.Exists(args[1]) ? File.ReadAllText(args[1]) : ""));
            return Task.FromResult(CommandResult.Ok());
        };
        return copies;
    }

    [Fact]
    public async Task WriteManifest_HashesFilesAndCopiesManifestIntoContainer()
    {
        var hash = new string('e', 64);
        _executor.QueuedResults.Enqueue(CommandResult.Ok($"{hash}  ./web/index.html\n"));
        _executor.QueuedResults.Enqueue(CommandResult.Ok("99 ./web/index.html\n"));
        var copies = CaptureDockerCopies();
        _ops.WriteManifest("artifacts", "2.0.0", "deadbeef");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(_options);

        result.Success.ShouldBeTrue();
        _executor.ExecutedCommands.Count.ShouldBe(2);
        _executor.ExecutedCommands[0].Args[1].ShouldContain("cd \"$1\"");
        _executor.ExecutedCommands[0].Args[^1].ShouldBe("/workspace/artifacts");
        _executor.ExecutedCommands[0].Args[1].ShouldContain("sha256sum");
        _executor.ExecutedCommands[1].Args[1].ShouldContain("stat -c");
        var copy = copies.ShouldHaveSingleItem();
        copy.Args[0].ShouldBe("cp");
        copy.Args[2].ShouldBe("container-1:/workspace/artifacts/manifest.json");
        var written = ArtifactManifest.Parse(copy.Content);
        written.Version.ShouldBe("2.0.0");
        written.Commit.ShouldBe("deadbeef");
        written.Files.ShouldBe([new ArtifactFile("web/index.html", 99, hash)]);
    }

    [Fact]
    public async Task WriteManifest_ManyFiles_PayloadIsNotAProcessArgument()
    {
        var hashes = new StringBuilder();
        var sizes = new StringBuilder();
        for (var i = 0; i < 5000; i++)
        {
            hashes.Append($"{new string('a', 64)}  ./dir/file-{i:D5}.txt\n");
            sizes.Append($"10 ./dir/file-{i:D5}.txt\n");
        }
        _executor.QueuedResults.Enqueue(CommandResult.Ok(hashes.ToString()));
        _executor.QueuedResults.Enqueue(CommandResult.Ok(sizes.ToString()));
        var copies = CaptureDockerCopies();
        _ops.WriteManifest("artifacts", "2.0.0", "deadbeef");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(_options);

        result.Success.ShouldBeTrue();
        ArtifactManifest.Parse(copies.ShouldHaveSingleItem().Content).Files.Count.ShouldBe(5000);
        _executor.ExecutedCommands.SelectMany(c => c.Args).ShouldAllBe(a => a.Length < 1000);
    }

    [Fact]
    public async Task WriteManifest_WithoutBuildContainer_FailsStep()
    {
        _executor.QueuedResults.Enqueue(CommandResult.Ok(""));
        _executor.QueuedResults.Enqueue(CommandResult.Ok(""));
        _ops.WriteManifest("artifacts", "2.0.0", "deadbeef");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(_options);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task ImportArtifacts_Step_CopiesCheckListIntoContainerInsteadOfPassingItAsArgument()
    {
        WriteArtifacts("artifacts", new() { ["a.txt"] = "A", ["b.txt"] = "B" });
        _options.Session.InvocationRoot = _dir;
        var copies = CaptureDockerCopies();
        _executor.QueuedResults.Enqueue(CommandResult.Ok());   // sha256sum -c
        _executor.QueuedResults.Enqueue(CommandResult.Ok("./a.txt\n./b.txt\n"));   // find
        _ops.ImportArtifacts("artifacts");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(_options);

        result.Success.ShouldBeTrue();
        var checkList = copies.Single(c => c.Args.Length > 2 && c.Args[2].Contains("/tmp/ando-check-"));
        checkList.Args[2].ShouldStartWith("container-1:/tmp/ando-check-");
        checkList.Content.ShouldContain($"{Sha("A")}  ./a.txt");
        var check = _executor.ExecutedCommands[0];
        check.Args[1].ShouldContain("sha256sum --quiet -c \"$1\"");
        check.Args.ShouldNotContain(a => a.Contains(Sha("A")));
    }

    [Fact]
    public async Task WriteManifest_HashFailure_FailsStep()
    {
        _executor.SimulateFailure = true;
        _ops.WriteManifest("artifacts", "2.0.0", "deadbeef");

        var result = await new WorkflowRunner(_registry, _logger).RunAsync(_options);

        result.Success.ShouldBeFalse();
    }
}
