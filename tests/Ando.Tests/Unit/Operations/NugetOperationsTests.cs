// =============================================================================
// NugetOperationsTests.cs
//
// Summary: Unit tests for NugetOperations class.
//
// Tests verify that:
// - Pack and Push operations register the correct steps
// - Steps execute the correct dotnet commands with proper arguments
// - Options (Version, Output, SkipDuplicates, etc.) are translated to CLI flags
// - EnsureAuthenticated captures API key from environment
// - Push with ProjectRef uses correct default package path
//
// Design: Uses MockExecutor to capture commands without execution,
// and TestLogger to verify logging behavior.
// =============================================================================

using Ando.Operations;
using Ando.References;
using Ando.Steps;
using Ando.Tests.TestFixtures;

namespace Ando.Tests.Unit.Operations;

[Trait("Category", "Unit")]
public class NugetOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private NugetOperations CreateNuget() =>
        new NugetOperations(_registry, _logger, () => _executor);

    [Fact]
    public void Pack_RegistersStep()
    {
        var nuget = CreateNuget();
        var project = ProjectRef.From("./src/MyLib/MyLib.csproj");

        nuget.Pack(project);

        Assert.Single(_registry.Steps);
        Assert.Equal("Nuget.Pack", _registry.Steps[0].Name);
        Assert.Equal("MyLib", _registry.Steps[0].Context);
    }

    [Fact]
    public void Push_WithPath_RegistersStep()
    {
        // Set up environment variable for API key.
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "test-key");
        try
        {
            var nuget = CreateNuget();

            nuget.Push("./packages/MyLib.1.0.0.nupkg");

            Assert.Single(_registry.Steps);
            Assert.Equal("Nuget.Push", _registry.Steps[0].Name);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public void Push_WithProject_RegistersStep()
    {
        // Set up environment variable for API key.
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "test-key");
        try
        {
            var nuget = CreateNuget();
            var project = ProjectRef.From("./src/MyLib/MyLib.csproj");

            nuget.Push(project);

            Assert.Single(_registry.Steps);
            Assert.Equal("Nuget.Push", _registry.Steps[0].Name);
            Assert.Equal("MyLib", _registry.Steps[0].Context);
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public async Task Pack_ExecutesCorrectCommand()
    {
        var nuget = CreateNuget();
        var project = ProjectRef.From("./src/MyLib/MyLib.csproj");

        nuget.Pack(project);

        await _registry.Steps[0].Execute();

        Assert.Single(_executor.ExecutedCommands);
        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("dotnet", command);
        Assert.Contains("pack", args);
        Assert.Contains("./src/MyLib/MyLib.csproj", args);
    }

    [Fact]
    public async Task Pack_WithVersion_IncludesVersionFlag()
    {
        var nuget = CreateNuget();
        var project = ProjectRef.From("./src/MyLib/MyLib.csproj");

        nuget.Pack(project, o => o.WithVersion("2.0.0"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        Assert.Contains("-p:PackageVersion", cmd.Args);
        Assert.Contains("2.0.0", cmd.Args);
    }

    [Fact]
    public async Task Pack_WithOutput_IncludesOutputFlag()
    {
        var nuget = CreateNuget();
        var project = ProjectRef.From("./src/MyLib/MyLib.csproj");

        nuget.Pack(project, o => o.Output("./packages"));

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        // Output path may be converted to absolute, so just check it contains the flag.
        Assert.True(cmd.HasArg("-o"));
        var outputPath = cmd.GetArgValue("-o");
        Assert.NotNull(outputPath);
        Assert.Contains("packages", outputPath);
    }

    [Fact]
    public async Task Pack_WithSymbols_IncludesSymbolsFlag()
    {
        var nuget = CreateNuget();
        var project = ProjectRef.From("./src/MyLib/MyLib.csproj");

        nuget.Pack(project, o => o.WithSymbols());

        await _registry.Steps[0].Execute();

        var cmd = _executor.ExecutedCommands[0];
        Assert.True(cmd.HasArg("--include-symbols"));
    }

    [Fact]
    public async Task Push_WithPath_ExecutesCorrectCommand()
    {
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "test-key");
        try
        {
            var nuget = CreateNuget();
            nuget.EnsureAuthenticated();

            nuget.Push("./packages/MyLib.1.0.0.nupkg");

            await _registry.Steps[0].Execute();

            var cmd = _executor.ExecutedCommands[0];
            Assert.Equal("dotnet", cmd.Command);
            Assert.True(cmd.HasArg("nuget"));
            Assert.True(cmd.HasArg("push"));
            Assert.True(cmd.HasArg("./packages/MyLib.1.0.0.nupkg"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public async Task Push_WithProject_UsesCorrectPackagePath()
    {
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "test-key");
        try
        {
            var nuget = CreateNuget();
            var project = ProjectRef.From("./src/MyLib/MyLib.csproj");
            nuget.EnsureAuthenticated();

            nuget.Push(project);

            await _registry.Steps[0].Execute();

            var cmd = _executor.ExecutedCommands[0];
            Assert.Equal("dotnet", cmd.Command);
            Assert.True(cmd.HasArg("nuget"));
            Assert.True(cmd.HasArg("push"));
            // Should use the project's bin/Release directory with glob pattern.
            Assert.Contains(cmd.Args, a => a.Contains("bin") && a.Contains("Release") && a.Contains("*.nupkg"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public async Task Push_WithSkipDuplicates_IncludesFlag()
    {
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "test-key");
        try
        {
            var nuget = CreateNuget();
            nuget.EnsureAuthenticated();

            nuget.Push("./packages/MyLib.nupkg", o => o.SkipDuplicates());

            await _registry.Steps[0].Execute();

            var cmd = _executor.ExecutedCommands[0];
            Assert.True(cmd.HasArg("--skip-duplicate"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public async Task Push_DefaultsToNuGetOrg()
    {
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "test-key");
        try
        {
            var nuget = CreateNuget();
            nuget.EnsureAuthenticated();

            nuget.Push("./packages/MyLib.nupkg");

            await _registry.Steps[0].Execute();

            var cmd = _executor.ExecutedCommands[0];
            Assert.Equal("https://api.nuget.org/v3/index.json", cmd.GetArgValue("--source"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public async Task Push_WithCustomSource_UsesSpecifiedSource()
    {
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "test-key");
        try
        {
            var nuget = CreateNuget();
            nuget.EnsureAuthenticated();

            nuget.Push("./packages/MyLib.nupkg", o => o.ToSource("https://my-feed.com/v3/index.json"));

            await _registry.Steps[0].Execute();

            var cmd = _executor.ExecutedCommands[0];
            Assert.Equal("https://my-feed.com/v3/index.json", cmd.GetArgValue("--source"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public void EnsureAuthenticated_WithEnvVar_CapturesKey()
    {
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "my-secret-key");
        try
        {
            var nuget = CreateNuget();

            // Should not throw when env var is set.
            nuget.EnsureAuthenticated();

            // Should log success.
            Assert.Contains(_logger.InfoMessages, m => m.Contains("NuGet API key configured"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public async Task Push_UsesApiKeyFromEnsureAuthenticated()
    {
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "captured-key");
        try
        {
            var nuget = CreateNuget();
            nuget.EnsureAuthenticated();

            nuget.Push("./packages/MyLib.nupkg");

            await _registry.Steps[0].Execute();

            var cmd = _executor.ExecutedCommands[0];
            Assert.Equal("captured-key", cmd.GetArgValue("--api-key"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }

    [Fact]
    public async Task Pack_Failure_ReturnsFalse()
    {
        var nuget = CreateNuget();
        var project = ProjectRef.From("./src/MyLib/MyLib.csproj");
        _executor.SimulateFailure = true;
        _executor.FailureMessage = "Pack failed";

        nuget.Pack(project);

        var result = await _registry.Steps[0].Execute();

        result.ShouldBeFalse();
    }

    [Fact]
    public async Task Push_Failure_ReturnsFalse()
    {
        Environment.SetEnvironmentVariable("NUGET_API_KEY", "test-key");
        try
        {
            var nuget = CreateNuget();
            _executor.SimulateFailure = true;
            _executor.FailureMessage = "Push failed";
            nuget.EnsureAuthenticated();

            nuget.Push("./packages/MyLib.nupkg");

            var result = await _registry.Steps[0].Execute();

            result.ShouldBeFalse();
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_API_KEY", null);
        }
    }
}
