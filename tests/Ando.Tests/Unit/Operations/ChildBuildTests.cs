// =============================================================================
// ChildBuildTests.cs
//
// Summary: Tests for Ando.Build child builds (ref inheritance, --yes, WithEnv,
// record link), Ando.StepTree and the self invocation helper. A scripted
// executor stands in for the child CLI process.
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Operations;
using Ando.Profiles;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;
using Ando.Utilities;
using Ando.Workflow;

namespace Ando.Tests.Unit.Operations;

/// <summary>Runs a callback instead of a process and records the calls.</summary>
internal sealed class ScriptedExecutor(Func<string, string[], CommandOptions?, CommandResult> run) : ICommandExecutor
{
    public List<ExecutedCommand> Calls { get; } = [];

    public Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null)
    {
        Calls.Add(new ExecutedCommand(command, args, options));
        return Task.FromResult(run(command, args, options));
    }

    public bool IsAvailable(string command) => true;
}

[Collection("SecretValues")]
[Trait("Category", "Unit")]
public class ChildBuildTests : IDisposable
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly BuildOptions _buildOptions = new();
    private readonly string _root = Directory.CreateTempSubdirectory("ando-childbuild-").FullName;

    public ChildBuildTests() => SecretValues.Clear();

    public void Dispose()
    {
        SecretValues.Clear();
        Directory.Delete(_root, recursive: true);
    }

    private AndoOperations Create(ScriptedExecutor executor) => new(
        _registry, _logger, p => Path.Combine("/worktree", p.TrimStart('.', '/')), _buildOptions,
        new ArtifactOperations(_logger), new ProfileRegistry())
    {
        HostExecutorFactory = () => executor,
        Self = new SelfInvocation("ando-test", ["cli.dll"])
    };

    private static ScriptedExecutor Succeeding() => new((_, _, _) => CommandResult.Ok());

    private static string Value(ExecutedCommand call, string flag) => call.GetArgValue(flag)!;

    // ---- path mapping --------------------------------------------------------

    [Theory]
    [InlineData("/workspace/website", "/wt/website", "abc", "/proj", "/proj/website")]
    [InlineData("/workspace", "/wt", "abc", "/proj", "/proj")]
    [InlineData("./website", "/wt/website", "abc", "/proj", "/proj/website")]
    [InlineData("/workspace/web/../api", "/wt/api", "abc", "/proj", "/proj/api")]
    [InlineData("/workspace/website", "/wt/website", null, "/proj", "/wt/website")]
    [InlineData("/workspace/website", "/wt/website", "abc", null, "/wt/website")]
    [InlineData("/workspacefoo/x", "/wt/x", "abc", "/proj", "/wt/x")]
    [InlineData("/elsewhere/x", "/elsewhere/x", "abc", "/proj", "/elsewhere/x")]
    public void MapChildHostPath_UnderRef_UsesInvocationRoot(
        string containerPath, string translated, string? gitRef, string? invocationRoot, string expected) =>
        AndoOperations.MapChildHostPath(containerPath, translated, gitRef, invocationRoot).ShouldBe(expected);

    [Fact]
    public async Task Build_UnderRef_RunsFromInvocationRootWithRefAndYes()
    {
        Directory.CreateDirectory(Path.Combine(_root, "website"));
        _buildOptions.Session.InvocationRoot = _root;
        _buildOptions.Session.Ref = "0123456789abcdef";
        _buildOptions.Session.AssumeYes = true;
        var executor = Succeeding();

        Create(executor).Build(new DirectoryRef("./website"));
        (await _registry.Steps[0].Execute()).ShouldBeTrue();

        var call = executor.Calls.Single();
        call.Options!.WorkingDirectory.ShouldBe(Path.Combine(_root, "website"));
        call.Args.Take(2).ShouldBe(["cli.dll", "run"]);
        call.HasArg("--yes").ShouldBeTrue();
        Value(call, "--ref").ShouldBe("0123456789abcdef");
    }

    [Fact]
    public async Task Build_WithoutRef_KeepsTranslatedPathAndDoesNotForwardRefOrYes()
    {
        _buildOptions.Session.InvocationRoot = _root;
        var executor = Succeeding();

        Create(executor).Build(new DirectoryRef("./website"));
        await _registry.Steps[0].Execute();

        var call = executor.Calls.Single();
        call.Options!.WorkingDirectory.ShouldBe(Path.Combine("/worktree", "website"));
        call.HasArg("--ref").ShouldBeFalse();
        call.HasArg("--yes").ShouldBeFalse();
    }

    [Fact]
    public async Task Build_WithFile_PutsFileArgumentBeforeRunAfterPrefix()
    {
        var executor = Succeeding();

        Create(executor).Build(new DirectoryRef("./website") / "deploy.csando");
        await _registry.Steps[0].Execute();

        executor.Calls.Single().Args.Take(4).ShouldBe(["cli.dll", "-f", "deploy.csando", "run"]);
        _registry.Steps[0].Name.ShouldBe("Ando.Build");
        _registry.Steps[0].Context.ShouldBe("deploy.csando");
    }

    // ---- argument building ---------------------------------------------------

    [Fact]
    public void BuildArgs_IncludesRefYesAndRecordPathFile()
    {
        var args = new AndoBuildOptions { Ref = "abc", AssumeYes = true, RecordPathFile = "/tmp/r.txt" }.BuildArgs();

        args.ShouldContain("--yes");
        args[Array.IndexOf(args, "--ref") + 1].ShouldBe("abc");
        args[Array.IndexOf(args, "--record-path-file") + 1].ShouldBe("/tmp/r.txt");
        new AndoBuildOptions().BuildArgs().ShouldBe(["run"]);
    }

    // ---- WithEnv -------------------------------------------------------------

    [Fact]
    public async Task WithEnv_SetsChildEnvironmentOnly_AndResetsTrackedKeys()
    {
        var name = "ANDO_TEST_CHILD_VALUE";
        Environment.GetEnvironmentVariable(name).ShouldBeNull();
        var executor = Succeeding();

        Create(executor).Build(new DirectoryRef("./website"), o => o.WithEnv(name, "plain-value").WithEnv("B_KEY_X", "v"));
        await _registry.Steps[0].Execute();

        var env = executor.Calls.Single().Options!.Environment;
        env[name].ShouldBe("plain-value");
        env[LoadedEnvironmentVariables.TrackedKeysEnvVar].ShouldBe($"{name}\nB_KEY_X");
        Environment.GetEnvironmentVariable(name).ShouldBeNull();
    }

    [Fact]
    public async Task WithoutWithEnv_TrackedKeysAreInherited()
    {
        var executor = Succeeding();

        Create(executor).Build(new DirectoryRef("./website"));
        await _registry.Steps[0].Execute();

        executor.Calls.Single().Options!.Environment.ContainsKey(LoadedEnvironmentVariables.TrackedKeysEnvVar).ShouldBeFalse();
        executor.Calls.Single().Options!.Environment.ContainsKey(SecretValues.SecretKeysEnvVar).ShouldBeFalse();
    }

    [Fact]
    public void WithEnv_SecretByNameOrFlag_IsRedactedAndListedForChild()
    {
        var options = new AndoBuildOptions()
            .WithEnv("CLOUDFLARE_API_TOKEN", "tokenvalue-123456")
            .WithEnv("ACCOUNT", "flagged-value-123456", secret: true)
            .WithEnv("PLAIN", "plain-value-123456");

        SecretValues.Redact("a tokenvalue-123456 b flagged-value-123456 c plain-value-123456")
            .ShouldBe("a [REDACTED] b [REDACTED] c plain-value-123456");
        options.ChildEnvironment()[SecretValues.SecretKeysEnvVar].ShouldBe("ACCOUNT\nCLOUDFLARE_API_TOKEN");
    }

    [Fact]
    public void WithEnv_MergesTheInheritedRedactList_WithItsOwn()
    {
        Environment.SetEnvironmentVariable(SecretValues.SecretKeysEnvVar, "PARENT_API_TOKEN\nSHARED_TOKEN");
        try
        {
            var options = new AndoBuildOptions()
                .WithEnv("OWN_API_TOKEN", "own-token-value-123")
                .WithEnv("SHARED_TOKEN", "shared-token-value-123");

            options.ChildEnvironment()[SecretValues.SecretKeysEnvVar]
                .ShouldBe("OWN_API_TOKEN\nPARENT_API_TOKEN\nSHARED_TOKEN");

            // Only non-secret WithEnv values: the inherited list is still handed on.
            new AndoBuildOptions().WithEnv("PLAIN", "x").ChildEnvironment()[SecretValues.SecretKeysEnvVar]
                .ShouldBe("PARENT_API_TOKEN\nSHARED_TOKEN");
        }
        finally
        {
            Environment.SetEnvironmentVariable(SecretValues.SecretKeysEnvVar, null);
        }
    }

    [Fact]
    public void ChildLikeSetup_ControlVariablesAreNotRedacted_SoTheNamesStayVisible()
    {
        Environment.SetEnvironmentVariable("MY_CHILD_API_TOKEN", "child-token-value-123");
        Environment.SetEnvironmentVariable(SecretValues.SecretKeysEnvVar, "MY_CHILD_API_TOKEN");
        Environment.SetEnvironmentVariable(LoadedEnvironmentVariables.TrackedKeysEnvVar, "MY_CHILD_API_TOKEN\nOTHER_VAR_1");
        Environment.SetEnvironmentVariable(RunLockSet.HeldLocksEnvVar, "deploy-lock-name");
        try
        {
            // What AndoCli does in the child, in this order.
            SecretValues.AddFromEnvironment();
            SecretValues.AddFromListedKeys();

            SecretValues.Redact("MY_CHILD_API_TOKEN=child-token-value-123").ShouldBe("MY_CHILD_API_TOKEN=[REDACTED]");
            SecretValues.Redact("keys: MY_CHILD_API_TOKEN OTHER_VAR_1 locks: deploy-lock-name")
                .ShouldBe("keys: MY_CHILD_API_TOKEN OTHER_VAR_1 locks: deploy-lock-name");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MY_CHILD_API_TOKEN", null);
            Environment.SetEnvironmentVariable(SecretValues.SecretKeysEnvVar, null);
            Environment.SetEnvironmentVariable(LoadedEnvironmentVariables.TrackedKeysEnvVar, null);
            Environment.SetEnvironmentVariable(RunLockSet.HeldLocksEnvVar, null);
        }
    }

    [Fact]
    public void WithEnv_RejectsInvalidNames()
    {
        Should.Throw<ArgumentException>(() => new AndoBuildOptions().WithEnv("A=B", "x"));
        Should.Throw<ArgumentException>(() => new AndoBuildOptions().WithEnv("", "x"));
    }

    [Fact]
    public void AddFromListedKeys_RegistersTheNamedValues()
    {
        Environment.SetEnvironmentVariable("ANDO_TEST_LISTED_A", "listed-secret-aaaa");
        Environment.SetEnvironmentVariable(SecretValues.SecretKeysEnvVar, "ANDO_TEST_LISTED_A\nANDO_TEST_MISSING");
        try
        {
            SecretValues.AddFromListedKeys();
            SecretValues.Redact("x listed-secret-aaaa").ShouldBe("x [REDACTED]");
        }
        finally
        {
            Environment.SetEnvironmentVariable("ANDO_TEST_LISTED_A", null);
            Environment.SetEnvironmentVariable(SecretValues.SecretKeysEnvVar, null);
        }
    }

    // ---- child record link ---------------------------------------------------

    [Fact]
    public async Task Build_LinksTheChildRecord_AndDeletesTheTempFolder()
    {
        string? recordFile = null;
        var executor = new ScriptedExecutor((_, args, _) =>
        {
            recordFile = args[Array.IndexOf(args, "--record-path-file") + 1];
            File.WriteAllText(recordFile, "/proj/website/.ando/runs/x.json\n");
            return CommandResult.Ok();
        });

        Create(executor).Build(new DirectoryRef("./website"));
        await _registry.Steps[0].Execute();

        _buildOptions.Session.Reports.ShouldBe([new("child record website", "/proj/website/.ando/runs/x.json")]);
        File.Exists(recordFile!).ShouldBeFalse();
        Directory.Exists(Path.GetDirectoryName(recordFile!)!).ShouldBeFalse();
        Path.GetDirectoryName(Path.GetDirectoryName(recordFile!)!).ShouldBe(Path.GetTempPath().TrimEnd('/'));
    }

    [Fact]
    public async Task Build_RecordKeys_UsePathRelativeToInvocationRoot_AndNumberRepeats()
    {
        _buildOptions.Session.InvocationRoot = "/proj";
        var executor = new ScriptedExecutor((_, args, _) =>
        {
            File.WriteAllText(args[Array.IndexOf(args, "--record-path-file") + 1], "/r/" + Guid.NewGuid() + ".json");
            return CommandResult.Ok();
        });
        var ando = new AndoOperations(
            _registry, _logger, p => "/proj/" + p.TrimStart('.', '/'), _buildOptions,
            new ArtifactOperations(_logger), new ProfileRegistry())
        { HostExecutorFactory = () => executor, Self = new SelfInvocation("ando-test", ["cli.dll"]) };

        ando.Build(new DirectoryRef("./a/web"));
        ando.Build(new DirectoryRef("./b/web"));
        ando.Build(new DirectoryRef("./b/web"));
        ando.Build(new DirectoryRef("./b/web") / "deploy.csando");
        foreach (var step in _registry.Steps) await step.Execute();

        _buildOptions.Session.Reports.Select(r => r.Key).ShouldBe(
            ["child record a/web/build.csando", "child record b/web/build.csando",
             "child record b/web/build.csando #2", "child record b/web/deploy.csando"]);
    }

    [Fact]
    public async Task Build_WithoutRecordFile_AddsNothing_AndFailureIsReported()
    {
        var executor = new ScriptedExecutor((_, _, _) => CommandResult.Failed(3));

        Create(executor).Build(new DirectoryRef("./website"));
        (await _registry.Steps[0].Execute()).ShouldBeFalse();

        _buildOptions.Session.Reports.ShouldBeEmpty();
    }

    // ---- StepTree ------------------------------------------------------------

    [Fact]
    public void StepTreeArgs_BuildsTheDryRunCommandLine()
    {
        AndoOperations.StepTreeArgs("/p/d.csando", null, null, "/t/tree.txt").ShouldBe(
            ["run", "--dry-run", "--no-env", "--no-color", "-f", "/p/d.csando", "--dry-run-output", "/t/tree.txt"]);
        AndoOperations.StepTreeArgs("/p/d.csando", "eu,prod", "abc", "/t/tree.txt").ShouldBe(
            ["run", "--dry-run", "--no-env", "--no-color", "-f", "/p/d.csando", "-p", "eu,prod", "--ref", "abc",
             "--dry-run-output", "/t/tree.txt"]);
    }

    [Theory]
    [InlineData("a\r\n  b\r\n", "a\n  b")]
    [InlineData("a\n  b\n\n", "a\n  b")]
    [InlineData("", "")]
    public void NormalizeTree_UsesLfAndKeepsIndentation(string text, string expected) =>
        AndoOperations.NormalizeTree(text).ShouldBe(expected);

    [Fact]
    public async Task StepTree_ReturnsTheTreeFile_RunsFromInvocationRoot()
    {
        _buildOptions.Session.InvocationRoot = _root;
        var executor = new ScriptedExecutor((_, args, _) =>
        {
            File.WriteAllText(args[Array.IndexOf(args, "--dry-run-output") + 1], "Dotnet.Build (a)\r\nAndo.Block: x\r\n  Shell.Run (y)\r\n");
            return CommandResult.Ok("ANDO banner that is ignored");
        });

        var tree = Create(executor).StepTree("deploy.csando", "eu");
        _registry.Steps[0].Name.ShouldBe("Ando.StepTree");
        _registry.Steps[0].Context.ShouldBe("deploy.csando -p eu");
        (await _registry.Steps[0].Execute()).ShouldBeTrue();

        tree.Value.ShouldBe("Dotnet.Build (a)\nAndo.Block: x\n  Shell.Run (y)");
        var call = executor.Calls.Single();
        call.Command.ShouldBe("ando-test");
        call.Options!.WorkingDirectory.ShouldBe(_root);
        call.Args.Take(3).ShouldBe(["cli.dll", "run", "--dry-run"]);
        Value(call, "-f").ShouldBe(Path.Combine(_root, "deploy.csando"));
        Value(call, "-p").ShouldBe("eu");
        call.HasArg("--ref").ShouldBeFalse();
    }

    [Fact]
    public async Task StepTree_UnderRef_PassesTheRef()
    {
        _buildOptions.Session.InvocationRoot = _root;
        _buildOptions.Session.Ref = "abc123";
        var executor = new ScriptedExecutor((_, args, _) =>
        {
            File.WriteAllText(args[Array.IndexOf(args, "--dry-run-output") + 1], "x");
            return CommandResult.Ok();
        });

        Create(executor).StepTree("d.csando");
        await _registry.Steps[0].Execute();

        Value(executor.Calls.Single(), "--ref").ShouldBe("abc123");
    }

    [Fact]
    public async Task StepTree_NonZeroExit_FailsAndLogsTheOutputTail()
    {
        var executor = new ScriptedExecutor((_, _, _) => CommandResult.Failed(2, output: "line1\nerror: bad script"));

        Create(executor).StepTree("d.csando");

        (await _registry.Steps[0].Execute()).ShouldBeFalse();
        _logger.ErrorMessages.ShouldContain(m => m.Contains("error: bad script"));
    }

    [Fact]
    public async Task StepTree_HonorsWithTimeout_AndWithEnv()
    {
        var executor = new ScriptedExecutor((_, args, _) =>
        {
            File.WriteAllText(args[Array.IndexOf(args, "--dry-run-output") + 1], "x");
            return CommandResult.Ok();
        });

        Create(executor).StepTree("d.csando").WithTimeout(TimeSpan.FromSeconds(7)).WithEnv("K", "v");
        await _registry.Steps[0].Execute();

        var options = executor.Calls.Single().Options!;
        options.TimeoutMs.ShouldBe(7000);
        options.Environment["K"].ShouldBe("v");
    }

    [Fact]
    public async Task StepTree_UsesTheRegistryDefaultTimeout()
    {
        _registry.DefaultCommandTimeoutMs = 12_000;
        var executor = new ScriptedExecutor((_, args, _) =>
        {
            File.WriteAllText(args[Array.IndexOf(args, "--dry-run-output") + 1], "x");
            return CommandResult.Ok();
        });

        Create(executor).StepTree("d.csando");
        await _registry.Steps[0].Execute();

        executor.Calls.Single().Options!.TimeoutMs.ShouldBe(12_000);
    }

    [Fact]
    public void WriteDryRunOutput_WritesTheLinesWithLf()
    {
        var file = Path.Combine(_root, "tree.txt");

        Ando.Cli.AndoCli.WriteDryRunOutput(file, _root, ["a", "  b"], _logger);

        File.ReadAllText(file).ShouldBe("a\n  b");
        Ando.Cli.AndoCli.WriteDryRunOutput(null, _root, ["x"], _logger).ShouldBeTrue();
    }

    [Fact]
    public void WriteDryRunOutput_FailedWrite_IsAnError()
    {
        var file = Path.Combine(_root, "missing-folder", "tree.txt");

        Ando.Cli.AndoCli.WriteDryRunOutput(file, _root, ["a"], _logger).ShouldBeFalse();

        _logger.ErrorMessages.ShouldContain(m => m.Contains("Could not write the step tree"));
    }
}

[Trait("Category", "Unit")]
public class SelfInvocationTests
{
    [Fact]
    public void Resolve_DotnetHost_UsesTheEntryAssembly() =>
        SelfInvocation.Resolve("/usr/share/dotnet/dotnet", "/src/Ando/bin/Ando.dll")
            .ShouldBe(new SelfInvocation("/usr/share/dotnet/dotnet", ["/src/Ando/bin/Ando.dll"]), new SelfInvocationComparer());

    [Fact]
    public void Resolve_NativeExecutable_UsesTheProcessPath()
    {
        var self = SelfInvocation.Resolve("/home/u/.dotnet/tools/ando", "/home/u/.dotnet/tools/.store/ando.dll");

        self.Command.ShouldBe("/home/u/.dotnet/tools/ando");
        self.PrefixArgs.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "/x/Ando.dll")]
    [InlineData("/usr/bin/dotnet", null)]
    [InlineData("/usr/bin/dotnet", "")]
    public void Resolve_UnknownInputs_FallBackToAndo(string? processPath, string? entry)
    {
        var self = SelfInvocation.Resolve(processPath, entry);

        self.Command.ShouldBe("ando");
        self.PrefixArgs.ShouldBeEmpty();
    }

    [Fact]
    public void WithArgs_PutsThePrefixFirst() =>
        new SelfInvocation("dotnet", ["Ando.dll"]).WithArgs(["-f", "x", "run"]).ShouldBe(["Ando.dll", "-f", "x", "run"]);

    private sealed class SelfInvocationComparer : IEqualityComparer<SelfInvocation>
    {
        public bool Equals(SelfInvocation? x, SelfInvocation? y) =>
            x!.Command == y!.Command && x.PrefixArgs.SequenceEqual(y.PrefixArgs);

        public int GetHashCode(SelfInvocation obj) => obj.Command.GetHashCode();
    }
}
