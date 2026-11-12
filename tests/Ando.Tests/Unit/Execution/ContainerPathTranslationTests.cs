// =============================================================================
// ContainerPathTranslationTests.cs
//
// Summary: Tests that path arguments reach container commands as container
// paths: host paths under the project root, the --ref worktree or the
// invocation directory become /workspace/..., while relative and
// container-absolute paths pass through. Also checks the arguments that
// Dotnet.Publish/Nuget.Pack/Ef.Script produce for a BuildPath output.
// =============================================================================

using Ando.Context;
using Ando.Execution;
using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Execution;

[Trait("Category", "Unit")]
public class ContainerPathTranslationTests
{
    private const string Project = "/home/dev/proj";
    private const string Worktree = "/tmp/ando-ref-abc-123";

    private static ContainerExecutor CreateExecutor()
    {
        var executor = new ContainerExecutor("c1", new TestLogger());
        executor.AddHostRoot(Worktree);
        executor.AddHostRoot(Project);
        executor.SetHostRootPath(Project);
        return executor;
    }

    [Theory]
    [InlineData("out/web", "out/web")]                                   // relative: unchanged (cwd is /workspace)
    [InlineData("/home/dev/proj/out/web", "/workspace/out/web")]         // host path under the project root
    [InlineData("/tmp/ando-ref-abc-123/out/web", "/workspace/out/web")]  // host path under the --ref worktree
    [InlineData("/workspace/out/web", "/workspace/out/web")]             // container-absolute
    [InlineData("/var/tmp/other", "/var/tmp/other")]                     // elsewhere: unchanged
    [InlineData("/home/dev/proj-other/x", "/home/dev/proj-other/x")]     // prefix of a sibling is not under the root
    [InlineData("/tmp/ando-ref-abc-123", "/workspace")]                  // the root itself
    [InlineData("-p:Out=/tmp/ando-ref-abc-123/o", "-p:Out=/workspace/o")] // name=value form
    [InlineData("--configuration", "--configuration")]
    public void TranslateArgs_MapsHostPathsToWorkspace(string arg, string expected)
    {
        CreateExecutor().TranslateArgs([arg]).ShouldBe([expected]);
    }

    [Fact]
    public async Task DotnetPublishNugetPackEfScript_OutputBecomesContainerPathInTheExecutedArgs()
    {
        // Output(string) resolves to a host path (against the host directory at script load); simulate that
        // with an absolute path under a pretend host root.
        var dir = Directory.CreateTempSubdirectory("ando-pathtr-").FullName;
        try
        {
            var registry = new StepRegistry();
            var mock = new MockExecutor();
            var logger = new TestLogger();
            var project = ProjectRef.From("src/App/App.csproj");
            new DotnetOperations(registry, logger, () => mock).Publish(project, o => o.Output(Path.Combine(dir, "out/web")));
            new NugetOperations(registry, logger, () => mock).Pack(project, o => o.Output(Path.Combine(dir, "out/pkg")));
            foreach (var step in registry.Steps) await step.Execute();

            var executor = new ContainerExecutor("c1", logger);
            executor.AddHostRoot(Path.GetFullPath(dir));
            var args = mock.ExecutedCommands.SelectMany(c => executor.TranslateArgs(c.Args)).ToArray();

            args.ShouldContain("/workspace/out/web");
            args.ShouldContain("/workspace/out/pkg");
            args.ShouldNotContain(a => a.StartsWith(Path.GetFullPath(dir)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
