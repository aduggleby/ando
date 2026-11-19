// =============================================================================
// ArtifactCopyFailureTests.cs
//
// Summary: A failed artifact copy (refused destination, undeletable destination,
// failed docker cp) is reported as failure, fails BuildContext's copy and is
// listed in the run record; and the Ando.CopyToHost step.
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Operations;
using Ando.Scripting;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class ArtifactCopyFailureTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("ando-copyfail-").FullName;
    private readonly TestLogger _logger = new();
    private readonly List<string[]> _dockerCalls = [];

    public void Dispose()
    {
        // Undo the read-only folder of the delete-failure test.
        foreach (var dir in Directory.GetDirectories(_root, "*", SearchOption.AllDirectories))
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(dir, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
        }
        Directory.Delete(_root, recursive: true);
    }

    private Func<string[], Task<CommandResult>> Docker(bool succeed, string error = "no such container") =>
        args =>
        {
            _dockerCalls.Add(args);
            if (succeed && args[0] == "cp")
            {
                File.WriteAllText(args[2], "copied");
            }
            return Task.FromResult(succeed ? CommandResult.Ok() : CommandResult.Failed(1, error));
        };

    private ArtifactOperations Artifacts(string containerPath, string hostPath)
    {
        var artifacts = new ArtifactOperations(_logger);
        artifacts.CopyToHost(containerPath, hostPath);
        return artifacts;
    }

    [Fact]
    public async Task CopyToHostAsync_Success_ReturnsNoFailures()
    {
        var failed = await Artifacts("/workspace/out.txt", "out.txt")
            .CopyToHostAsync("cid", _root, _logger, Docker(true));

        Assert.Empty(failed);
        Assert.Empty(_logger.ErrorMessages);
        Assert.True(File.Exists(Path.Combine(_root, "out.txt")));
    }

    [Fact]
    public async Task CopyToHostAsync_FailedDockerCp_ReportsFailureAsError()
    {
        var failed = await Artifacts("/workspace/out.txt", "out.txt")
            .CopyToHostAsync("cid", _root, _logger, Docker(false, "permission denied"));

        Assert.Equal(["/workspace/out.txt"], failed);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("permission denied") && m.Contains("/workspace/out.txt"));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task CopyToHostAsync_RefusedDestination_ReportsFailureAndRunsNoDocker(string hostPath)
    {
        var project = Path.Combine(_root, "project");
        Directory.CreateDirectory(project);

        var failed = await Artifacts("/workspace/out", hostPath)
            .CopyToHostAsync("cid", project, _logger, Docker(true));

        Assert.Single(failed);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Refusing"));
        Assert.Empty(_dockerCalls);
    }

    [Fact]
    public async Task CopyToHostAsync_UndeletableDestination_ReportsFailure()
    {
        if (OperatingSystem.IsWindows() || Environment.UserName == "root") return;

        var locked = Path.Combine(_root, "locked");
        Directory.CreateDirectory(locked);
        File.WriteAllText(Path.Combine(locked, "out.txt"), "old");
        File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserExecute);

        var failed = await Artifacts("/workspace/out.txt", "locked/out.txt")
            .CopyToHostAsync("cid", _root, _logger, Docker(true));

        Assert.Single(failed);
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Could not remove existing destination"));
        Assert.Empty(_dockerCalls);
    }

    [Fact]
    public async Task BuildContext_FailedCopy_ReturnsFalseAndMarksSession()
    {
        var context = new BuildContext("/workspace", _logger);
        context.Artifacts.CopyToHost("/workspace/out.txt", "out.txt");
        context.SetDockerManager(new DockerManager(_logger), "cid", _root);
        context.Options.Session.DockerRunner = Docker(false);

        var ok = await context.CopyArtifactsToHostAsync();

        Assert.False(ok);
        Assert.True(context.Options.Session.ArtifactCopyFailed);
        Assert.Contains(context.Options.Session.Reports, r => r.Key == "artifact-copy-failed" && r.Value == "/workspace/out.txt");
        Assert.DoesNotContain(context.Options.Session.Reports, r => r.Key.StartsWith("artifact /"));
    }

    [Fact]
    public async Task BuildContext_SuccessfulCopy_ReturnsTrue()
    {
        var context = new BuildContext("/workspace", _logger);
        context.Artifacts.CopyToHost("/workspace/out.txt", "out.txt");
        context.SetDockerManager(new DockerManager(_logger), "cid", _root);
        context.Options.Session.DockerRunner = Docker(true);

        Assert.True(await context.CopyArtifactsToHostAsync());
        Assert.False(context.Options.Session.ArtifactCopyFailed);
    }

    // Ando.CopyToHost step

    private (AndoOperations Ando, StepRegistry Registry, RunSession Session) NewAndo()
    {
        var registry = new StepRegistry();
        var options = new BuildOptions();
        options.Session.InvocationRoot = _root;
        options.Session.ContainerId = "cid";
        var ando = new AndoOperations(registry, _logger, p => p, options, new ArtifactOperations(_logger),
            new Ando.Profiles.ProfileRegistry());
        return (ando, registry, options.Session);
    }

    [Fact]
    public void CopyToHost_RegistersStepWithNameAndContext()
    {
        var (ando, registry, _) = NewAndo();

        ando.CopyToHost("artifacts/manifest.json", "out/manifest.json");

        var step = Assert.Single(registry.Steps);
        Assert.Equal("Ando.CopyToHost", step.Name);
        Assert.Equal("artifacts/manifest.json -> out/manifest.json", step.Context);
    }

    [Fact]
    public async Task CopyToHostStep_ResolvesRelativeToInvocationRootAndReplacesDestination()
    {
        var (_, _, session) = NewAndo();
        session.DockerRunner = Docker(true);
        var dest = Path.Combine(_root, "out", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(dest, "old");

        var ok = await AndoOperations.CopyToHostAsync(session, "artifacts/manifest.json", "out/manifest.json", _logger);

        Assert.True(ok);
        Assert.Equal("copied", File.ReadAllText(dest));
        Assert.Equal(["cp", "cid:/workspace/artifacts/manifest.json", dest], _dockerCalls.Single());
    }

    [Fact]
    public async Task CopyToHostStep_UnderRef_UsesInvocationRootNotWorktree()
    {
        var (_, _, session) = NewAndo();
        session.SourceRoot = Path.Combine(_root, "worktree");
        session.Ref = "abc";
        session.DockerRunner = Docker(true);

        Assert.True(await AndoOperations.CopyToHostAsync(session, "a.txt", "a.txt", _logger));

        Assert.True(File.Exists(Path.Combine(_root, "a.txt")));
    }

    [Theory]
    [InlineData(".")]
    [InlineData("..")]
    public async Task CopyToHostStep_RefusesProjectRootAndAncestors(string hostPath)
    {
        var (_, _, session) = NewAndo();
        session.InvocationRoot = Path.Combine(_root, "project");
        Directory.CreateDirectory(session.InvocationRoot);
        session.DockerRunner = Docker(true);

        Assert.False(await AndoOperations.CopyToHostAsync(session, "out", hostPath, _logger));

        Assert.Contains(_logger.ErrorMessages, m => m.Contains("Refusing"));
        Assert.Empty(_dockerCalls);
    }

    [Fact]
    public async Task CopyToHostStep_FailedDockerCp_FailsTheStep()
    {
        var (_, _, session) = NewAndo();
        session.DockerRunner = Docker(false, "No such container:path");

        Assert.False(await AndoOperations.CopyToHostAsync(session, "missing", "x", _logger));
        Assert.Contains(_logger.ErrorMessages, m => m.Contains("No such container:path"));
    }

    [Fact]
    public async Task CopyToHostStep_WithoutContainer_Fails()
    {
        var (_, _, session) = NewAndo();
        session.ContainerId = null;

        Assert.False(await AndoOperations.CopyToHostAsync(session, "a", "b", _logger));
    }
}
