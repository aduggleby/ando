using Ando.Scripting;
using Ando.Tests.TestFixtures;
using Ando.Workflow;

namespace Ando.Tests.Unit.Scripting;

[Trait("Category", "Unit")]
public class ScriptHostTests
{
    private readonly TestLogger _logger = new();

    [Fact]
    public async Task LoadScriptAsync_ThrowsIfFileNotFound()
    {
        var host = new ScriptHost(_logger);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => host.LoadScriptAsync("/nonexistent/build.ando", "/nonexistent"));
    }

    [Fact]
    public async Task LoadScriptAsync_LoadsSimpleScript()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.ando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                var project = Project.From("./src/Test/Test.csproj");
                Dotnet.Build(project);
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            Assert.Single(context.StepRegistry.Steps);
            Assert.Equal("Dotnet.Build", context.StepRegistry.Steps[0].Name);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_ExposesContextPaths()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.ando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                Context.Vars["rootPath"] = Context.Paths.Root.Value;
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            Assert.Equal(tempDir, context.Context.Vars["rootPath"]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_ExposesOptions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.ando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                Options.UseConfiguration(c => c.Release);
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            Assert.Equal(Configuration.Release, context.Options.Configuration);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_ExposesRootShorthand()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.ando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                var distPath = Root / "dist";
                Context.Vars["distPath"] = distPath.Value;
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            var expectedPath = Path.Combine(tempDir, "dist");
            Assert.Equal(expectedPath, context.Context.Vars["distPath"]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_HandlesCompilationErrors()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.ando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                var x = invalid syntax here
                """);

            var host = new ScriptHost(_logger);

            await Assert.ThrowsAsync<Microsoft.CodeAnalysis.Scripting.CompilationErrorException>(
                () => host.LoadScriptAsync(scriptPath, tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_ReturnsValidContext()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.ando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, "// empty script");

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            context.ShouldNotBeNull();
            context.StepRegistry.ShouldNotBeNull();
            context.Context.ShouldNotBeNull();
            context.Options.ShouldNotBeNull();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_ExposesProjectFromHelper()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.ando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                var project = Project.From("./src/MyApp/MyApp.csproj");
                Context.Vars["projectName"] = project.Name;
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            Assert.Equal("MyApp", context.Context.Vars["projectName"]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
