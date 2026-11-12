// =============================================================================
// DotnetOperationsTests.cs
//
// Summary: Unit tests for DotnetOperations class.
//
// Tests verify that:
// - Each operation (Restore, Build, Test, Publish) registers the correct step
// - Steps execute the correct dotnet commands with proper arguments
// - Options (Configuration, Runtime, etc.) are translated to CLI flags
// - Failure cases return false without throwing
//
// Design: Uses MockExecutor to capture commands without execution,
// and TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class DotnetOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private DotnetOperations CreateDotnet() =>
        new DotnetOperations(_registry, _logger, () => _executor);

    [Fact]
    public void Restore_RegistersStep()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Restore(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Dotnet.Restore", _registry.Steps[0].Name);
        Assert.Equal("MyApp", _registry.Steps[0].Context);
    }

    [Fact]
    public void Build_RegistersStep()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Build(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Dotnet.Build", _registry.Steps[0].Name);
    }

    [Fact]
    public void Test_RegistersStep()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Test(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Dotnet.Test", _registry.Steps[0].Name);
    }

    [Fact]
    public void Publish_RegistersStep()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Publish(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Dotnet.Publish", _registry.Steps[0].Name);
    }

    [Fact]
    public void Publish_WithOptions_AcceptsConfiguration()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Publish(project, o => o.WithConfiguration(Configuration.Release));

        Assert.Single(_registry.Steps);
    }

    [Fact]
    public async Task AllOperations_ExecuteSuccessfully()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Restore(project);
        dotnet.Build(project);
        dotnet.Test(project);
        dotnet.Publish(project);

        foreach (var step in _registry.Steps)
        {
            var result = await step.Execute();
            Assert.True(result);
        }
    }

    [Fact]
    public async Task Build_ExecutesCorrectCommand()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Build(project, o => o.Configuration = Configuration.Release);

        await _registry.Steps[0].Execute();

        Assert.Single(_executor.ExecutedCommands);
        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("build", args);
        Assert.Contains("-c", args);
        Assert.Contains("Release", args);
    }

    [Fact]
    public async Task Build_Failure_ReturnsFalse()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Build failed";

        dotnet.Build(project);

        var result = await _registry.Steps[0].Execute();

        result.ShouldBeFalse();
        // The operation doesn't log errors, it just returns false
        // Error logging is handled by the workflow runner
    }

    [Fact]
    public async Task Build_WithConfiguration_UsesCorrectFlag()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Build(project, o => o.Configuration = Configuration.Debug);

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        Assert.Equal("Debug", cmd.GetArgValue("-c"));
    }

    [Fact]
    public async Task Restore_ExecutesCorrectCommand()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Restore(project);

        await _registry.Steps[0].Execute();

        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("restore", args);
        Assert.Contains("./src/MyApp/MyApp.csproj", args);
    }

    [Fact]
    public async Task Test_ExecutesCorrectCommand()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp.Tests/MyApp.Tests.csproj");

        dotnet.Test(project);

        await _registry.Steps[0].Execute();

        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("test", args);
        Assert.Contains("./src/MyApp.Tests/MyApp.Tests.csproj", args);
    }

    [Fact]
    public async Task Publish_ExecutesCorrectCommand()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

        dotnet.Publish(project, o => o
            .WithConfiguration(Configuration.Release)
            .WithRuntime("linux-x64")
            .AsSelfContained());

        await _registry.Steps[0].Execute();

        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("publish", args);
        Assert.Contains("Release", args);
        Assert.Contains("linux-x64", args);
        Assert.Contains("--self-contained", args);
    }

    // DotnetTool tests

    [Fact]
    public void Tool_CreatesToolWithPackageId()
    {
        var dotnet = CreateDotnet();

        var tool = dotnet.Tool("dotnet-ef");

        tool.PackageId.ShouldBe("dotnet-ef");
    }

    [Fact]
    public void Tool_CreatesToolWithVersion()
    {
        var dotnet = CreateDotnet();

        var tool = dotnet.Tool("dotnet-ef", "8.0.0");

        tool.PackageId.ShouldBe("dotnet-ef");
        tool.Version.ShouldBe("8.0.0");
    }

    [Fact]
    public void Tool_WithoutVersion_HasNullVersion()
    {
        var dotnet = CreateDotnet();

        var tool = dotnet.Tool("dotnet-ef");

        tool.Version.ShouldBeNull();
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_ExecutesDotnetToolInstall()
    {
        var dotnet = CreateDotnet();
        var tool = dotnet.Tool("dotnet-ef");

        await tool.EnsureInstalledAsync();

        _executor.ExecutedCommands.ShouldHaveSingleItem();
        var cmd = _executor.ExecutedCommands[0];
        cmd.Command.ShouldBe("dotnet");
        cmd.HasArg("tool").ShouldBeTrue();
        cmd.HasArg("install").ShouldBeTrue();
        cmd.HasArg("dotnet-ef").ShouldBeTrue();
        cmd.HasArg("--global").ShouldBeTrue();
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_IncludesVersionWhenSpecified()
    {
        var dotnet = CreateDotnet();
        var tool = dotnet.Tool("dotnet-ef", "8.0.0");

        await tool.EnsureInstalledAsync();

        var cmd = _executor.ExecutedCommands[0];
        cmd.HasArg("--version").ShouldBeTrue();
        cmd.HasArg("8.0.0").ShouldBeTrue();
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_OnlyInstallsOnce()
    {
        var dotnet = CreateDotnet();
        var tool = dotnet.Tool("dotnet-ef");

        await tool.EnsureInstalledAsync();
        await tool.EnsureInstalledAsync();
        await tool.EnsureInstalledAsync();

        // Should only execute once
        _executor.ExecutedCommands.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_LogsInstallation()
    {
        var dotnet = CreateDotnet();
        var tool = dotnet.Tool("dotnet-ef", "8.0.0");

        await tool.EnsureInstalledAsync();

        _logger.InfoMessages.ShouldContain(m => m.Contains("dotnet-ef") && m.Contains("v8.0.0"));
    }

    [Fact]
    public async Task Tool_EnsureInstalledAsync_OnFailure_LogsWarning()
    {
        var dotnet = CreateDotnet();
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Installation failed";
        var tool = dotnet.Tool("dotnet-ef");

        await tool.EnsureInstalledAsync();

        _logger.WarningMessages.ShouldContain(m => m.Contains("dotnet-ef"));
    }

    [Fact]
    public async Task Publish_ExactCommand_DefaultsToNothingImplicit()
    {
        CreateDotnet().Publish(ProjectRef.From("./src/MyApp/MyApp.csproj"));

        await _registry.Steps[0].Execute();

        _executor.LastCommand!.CommandLine.ShouldBe("dotnet publish ./src/MyApp/MyApp.csproj");
    }

    [Fact]
    public async Task Publish_ReleaseOutputNoLogo_MatchesExpectedCommandLine()
    {
        CreateDotnet().Publish(ProjectRef.From("src/Web/Web.csproj"), o => o
            .WithConfiguration(Configuration.Release).Output("artifacts/web").WithNoLogo());

        await _registry.Steps[0].Execute();

        _executor.LastCommand!.CommandLine.ShouldBe("dotnet publish src/Web/Web.csproj -c Release -o /workspace/artifacts/web --nologo");
    }

    [Fact]
    public async Task Publish_NonZeroExit_FailsStep()
    {
        _executor.SimulateFailure = true;
        CreateDotnet().Publish(ProjectRef.From("src/Web/Web.csproj"));

        (await _registry.Steps[0].Execute()).ShouldBeFalse();
    }

    [Fact]
    public async Task OtherDotnetSteps_ExactCommands()
    {
        var dotnet = CreateDotnet();
        var project = ProjectRef.From("src/App/App.csproj");
        dotnet.Restore(project, o => { o.NoCache = true; o.WithRuntime("linux-x64"); });
        dotnet.Build(project, o => { o.Configuration = Configuration.Release; o.NoRestore = true; });
        dotnet.Test(project, o => { o.Configuration = Configuration.Release; o.NoBuild = true; o.Filter = "Category=Unit"; });

        foreach (var step in _registry.Steps) await step.Execute();

        _executor.ExecutedCommands.Select(c => c.CommandLine).ToArray().ShouldBe([
            "dotnet restore src/App/App.csproj --no-cache -r linux-x64",
            "dotnet build src/App/App.csproj -c Release --no-restore",
            "dotnet test src/App/App.csproj -c Release --no-build --filter Category=Unit"
        ]);
    }

    [Fact]
    public void ReadVersion_ReadsLiteralVersionRelativeToSourceRoot()
    {
        var root = Directory.CreateTempSubdirectory("ando-readversion-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Directory.Build.props"),
                "<Project><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "empty.props"),
                "<Project><PropertyGroup><Version> </Version></PropertyGroup></Project>");
            File.WriteAllText(Path.Combine(root, "two.props"),
                "<Project><PropertyGroup><Version>1</Version></PropertyGroup><PropertyGroup><Version>2</Version></PropertyGroup></Project>");
            var dotnet = new DotnetOperations(_registry, _logger, () => _executor, null, new RunSession { SourceRoot = root });

            dotnet.ReadVersion().ShouldBe("1.2.3");
            Should.Throw<InvalidOperationException>(() => dotnet.ReadVersion("empty.props")).Message.ShouldContain("empty");
            Should.Throw<InvalidOperationException>(() => dotnet.ReadVersion("two.props")).Message.ShouldContain("expected exactly one");
            Should.Throw<FileNotFoundException>(() => dotnet.ReadVersion("missing.props")).Message.ShouldContain("missing.props");
            _registry.Steps.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private async Task<string> RunLastAsync(Action<DotnetOperations> register, string? sha)
    {
        _executor.ExecutedCommands.Clear();
        var registry = new StepRegistry();
        register(new DotnetOperations(registry, _logger, () => _executor, null, new RunSession { CommitResolver = () => sha }));
        await registry.Steps[0].Execute();
        return _executor.LastCommand!.CommandLine;
    }

    private const string Sha = "0123456789abcdef0123456789abcdef01234567";

    [Fact]
    public async Task EveryRun_BuildPublishTest_AddSourceRevisionId()
    {
        var project = ProjectRef.From("a.csproj");
        (await RunLastAsync(d => d.Build(project), Sha)).ShouldBe($"dotnet build a.csproj -p:SourceRevisionId={Sha}");
        (await RunLastAsync(d => d.Publish(project), Sha)).ShouldBe($"dotnet publish a.csproj -p:SourceRevisionId={Sha}");
        (await RunLastAsync(d => d.Test(project), Sha)).ShouldBe($"dotnet test a.csproj -p:SourceRevisionId={Sha}");
    }

    [Fact]
    public async Task TestAndPublishWithNoBuild_AddNothing()
    {
        var project = ProjectRef.From("a.csproj");
        (await RunLastAsync(d => d.Test(project, o => o.NoBuild = true), Sha)).ShouldBe("dotnet test a.csproj --no-build");
        (await RunLastAsync(d => d.Publish(project, o => o.SkipBuild()), Sha)).ShouldBe("dotnet publish a.csproj --no-build");
    }

    [Fact]
    public async Task OutsideGit_AddsNoSourceRevisionId()
    {
        var project = ProjectRef.From("a.csproj");
        (await RunLastAsync(d => d.Build(project), null)).ShouldBe("dotnet build a.csproj");
        (await RunLastAsync(d => d.Publish(project), null)).ShouldBe("dotnet publish a.csproj");
    }

    [Fact]
    public async Task ExplicitSourceRevision_WinsAndIsNotDuplicated()
    {
        var project = ProjectRef.From("a.csproj");
        var build = await RunLastAsync(d => d.Build(project, o => o.WithSourceRevision("abc")), Sha);
        build.ShouldBe("dotnet build a.csproj -p:SourceRevisionId=abc");
        var publish = await RunLastAsync(d => d.Publish(project, o => o.WithSourceRevision("abc")), Sha);
        publish.ShouldBe("dotnet publish a.csproj -p:SourceRevisionId=abc");
        (await RunLastAsync(d => d.Test(project, o => o.WithSourceRevision("abc")), Sha)).ShouldBe("dotnet test a.csproj -p:SourceRevisionId=abc");
        // Explicit also works outside git.
        (await RunLastAsync(d => d.Build(project, o => o.WithSourceRevision("abc")), null)).ShouldBe("dotnet build a.csproj -p:SourceRevisionId=abc");
    }

    [Fact]
    public void SourceRevision_RefWinsAndRealGitHeadIsUsedAndOutsideGitIsNull()
    {
        new RunSession { Ref = "refsha", CommitResolver = () => "head" }.SourceRevision.ShouldBe("refsha");

        var notGit = Directory.CreateTempSubdirectory("ando-nogit-").FullName;
        try
        {
            new RunSession { SourceRoot = notGit }.SourceRevision.ShouldBeNull();
        }
        finally
        {
            Directory.Delete(notGit, recursive: true);
        }

        var repo = Directory.CreateTempSubdirectory("ando-srcrev-").FullName;
        try
        {
            void Git(params string[] args)
            {
                var info = new System.Diagnostics.ProcessStartInfo("git", ["-c", "user.name=T", "-c", "user.email=t@e.com", "-c", "commit.gpgsign=false", .. args])
                { WorkingDirectory = repo, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                using var p = System.Diagnostics.Process.Start(info)!;
                p.StandardOutput.ReadToEnd();
                p.StandardError.ReadToEnd();
                p.WaitForExit();
                p.ExitCode.ShouldBe(0);
            }
            Git("init", "-q");
            File.WriteAllText(Path.Combine(repo, "f"), "x");
            Git("add", "-A");
            Git("commit", "-q", "-m", "one");

            var viaGit = new RunSession { SourceRoot = repo }.SourceRevision;
            viaGit.ShouldNotBeNull();
            viaGit.Length.ShouldBe(40);
        }
        finally
        {
            Directory.Delete(repo, recursive: true);
        }
    }

    [Fact]
    public async Task WithoutSourceRevision_SuppressesAutomaticValue_ExplicitStillWins()
    {
        var project = ProjectRef.From("a.csproj");
        (await RunLastAsync(d => d.Build(project, o => o.WithoutSourceRevision()), Sha)).ShouldBe("dotnet build a.csproj");
        (await RunLastAsync(d => d.Test(project, o => o.WithoutSourceRevision()), Sha)).ShouldBe("dotnet test a.csproj");
        (await RunLastAsync(d => d.Publish(project, o => o.WithoutSourceRevision()), Sha)).ShouldBe("dotnet publish a.csproj");
        (await RunLastAsync(d => d.Publish(project, o => o.WithoutSourceRevision().WithSourceRevision("abc")), Sha))
            .ShouldBe("dotnet publish a.csproj -p:SourceRevisionId=abc");
    }

    [Fact]
    public async Task WithSourceRevision_TrimsAndRejectsBlank()
    {
        var project = ProjectRef.From("a.csproj");
        (await RunLastAsync(d => d.Build(project, o => o.WithSourceRevision("  abc \n")), null)).ShouldBe("dotnet build a.csproj -p:SourceRevisionId=abc");
        Should.Throw<ArgumentException>(() => new DotnetBuildOptions().WithSourceRevision(" "));
        Should.Throw<ArgumentException>(() => new DotnetTestOptions().WithSourceRevision(""));
        Should.Throw<ArgumentException>(() => new DotnetPublishOptions().WithSourceRevision("\t"));
        Should.Throw<ArgumentException>(() => new NugetPackOptions().WithSourceRevision(" "));
        new NugetPackOptions().WithSourceRevision(" x ").SourceRevision.ShouldBe("x");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("0123456789abcdef0123456789abcdef01234567")]
    public async Task PublishAndPackOutput_ResolvesInTheContainer_NormalAndRefSessions(string? refSha)
    {
        var session = new RunSession { Ref = refSha, SourceRoot = refSha == null ? "/home/dev/proj" : "/tmp/ando-ref-abc" };
        var root = new Ando.Context.PathsContext("/workspace").Root;
        string Publish(Action<DotnetPublishOptions> o)
        {
            var registry = new StepRegistry();
            var executor = new MockExecutor();
            new DotnetOperations(registry, _logger, () => executor, null, session)
                .Publish(ProjectRef.From("a.csproj"), o + (x => x.WithoutSourceRevision()));
            registry.Steps[0].Execute().GetAwaiter().GetResult();
            return executor.LastCommand!.CommandLine;
        }
        string Pack(Action<NugetPackOptions> o)
        {
            var registry = new StepRegistry();
            var executor = new MockExecutor();
            new NugetOperations(registry, _logger, () => executor, session)
                .Pack(ProjectRef.From("a.csproj"), o + (x => x.WithoutSourceRevision()));
            registry.Steps[0].Execute().GetAwaiter().GetResult();
            return executor.LastCommand!.CommandLine;
        }

        Publish(o => o.Output("out/x")).ShouldBe("dotnet publish a.csproj -o /workspace/out/x");
        Publish(o => o.Output("./out/x")).ShouldBe("dotnet publish a.csproj -o /workspace/out/x");
        Publish(o => o.Output("/workspace/y")).ShouldBe("dotnet publish a.csproj -o /workspace/y");
        Publish(o => o.Output(root / "z")).ShouldBe("dotnet publish a.csproj -o /workspace/z");
        Pack(o => o.Output("out/x")).ShouldBe("dotnet pack a.csproj -o /workspace/out/x");
        Pack(o => o.Output("/workspace/y")).ShouldBe("dotnet pack a.csproj -o /workspace/y");
        Pack(o => o.Output(root / "z")).ShouldBe("dotnet pack a.csproj -o /workspace/z");
        await Task.CompletedTask;
    }
}
