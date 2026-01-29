// =============================================================================
// ScriptHostTests.cs
//
// Summary: Unit tests for ScriptHost class.
//
// Tests verify Roslyn script loading, build context creation,
// and script execution behavior.
// =============================================================================

using Ando.Scripting;
using Ando.Tests.TestFixtures;

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
            () => host.LoadScriptAsync("/nonexistent/build.csando", "/nonexistent"));
    }

    [Fact]
    public async Task LoadScriptAsync_LoadsSimpleScript()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                var project = Dotnet.Project("./src/Test/Test.csproj");
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
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            // Verify Root.Value is accessible and correct
            await File.WriteAllTextAsync(scriptPath, """
                var rootPath = Root.Value;
                if (rootPath == null) throw new Exception("Root.Value is null");
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            Assert.Equal(tempDir, context.Context.Paths.Root.Value);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_ExposesAndoOperations()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                Ando.UseImage("mcr.microsoft.com/dotnet/sdk:9.0");
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            Assert.Equal("mcr.microsoft.com/dotnet/sdk:9.0", context.Options.Image);
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
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            // Test that Root / "subdir" path combining works
            await File.WriteAllTextAsync(scriptPath, """
                var distPath = Root / "dist";
                var expected = System.IO.Path.Combine(Root.Value, "dist");
                if (distPath.Value != expected) throw new Exception($"Expected {expected} but got {distPath.Value}");
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            // Script completed without throwing, path combining works
            Assert.NotNull(context);
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
        var scriptPath = Path.Combine(tempDir, "build.csando");

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
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, "// empty script");

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            context.ShouldNotBeNull();
            context.StepRegistry.ShouldNotBeNull();
            context.Context.ShouldNotBeNull();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_ExposesProjectFunction()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            // Test that Dotnet.Project() returns a correctly named ProjectRef
            await File.WriteAllTextAsync(scriptPath, """
                var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
                if (project.Name != "MyApp") throw new Exception($"Expected MyApp but got {project.Name}");
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            // Script completed without throwing, project name extraction works
            Assert.NotNull(context);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LoadScriptAsync_ExposesDirectoryFunction()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            // Test that Directory() returns a correctly named DirectoryRef
            await File.WriteAllTextAsync(scriptPath, """
                var frontend = Directory("./frontend");
                if (frontend.Name != "frontend") throw new Exception($"Expected frontend but got {frontend.Name}");
                """);

            var host = new ScriptHost(_logger);
            var context = await host.LoadScriptAsync(scriptPath, tempDir);

            // Script completed without throwing, directory name extraction works
            Assert.NotNull(context);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // =============================================================================
    // VerifyScriptAsync Tests - Script verification without execution
    // =============================================================================

    [Fact]
    public async Task VerifyScriptAsync_ValidScript_ReturnsNoErrors()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                var project = Dotnet.Project("./src/Test/Test.csproj");
                Dotnet.Build(project);
                """);

            var host = new ScriptHost(_logger);
            var errors = await host.VerifyScriptAsync(scriptPath);

            errors.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyScriptAsync_InvalidSyntax_ReturnsErrors()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                var x = invalid syntax here
                """);

            var host = new ScriptHost(_logger);
            var errors = await host.VerifyScriptAsync(scriptPath);

            errors.ShouldNotBeEmpty();
            errors.ShouldContain(e => e.Contains("CS"));  // Should contain C# error code
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyScriptAsync_MissingFile_ReturnsError()
    {
        var host = new ScriptHost(_logger);
        var errors = await host.VerifyScriptAsync("/nonexistent/build.csando");

        errors.ShouldNotBeEmpty();
        errors.ShouldContain(e => e.Contains("not found"));
    }

    [Fact]
    public async Task VerifyScriptAsync_EmptyScript_ReturnsNoErrors()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, "// empty script");

            var host = new ScriptHost(_logger);
            var errors = await host.VerifyScriptAsync(scriptPath);

            errors.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyScriptAsync_UndefinedVariable_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            await File.WriteAllTextAsync(scriptPath, """
                var x = undefinedVariable;
                """);

            var host = new ScriptHost(_logger);
            var errors = await host.VerifyScriptAsync(scriptPath);

            errors.ShouldNotBeEmpty();
            errors.ShouldContain(e => e.Contains("CS0103"));  // Name does not exist
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task VerifyScriptAsync_UsesAndoApi_Compiles()
    {
        // Verify that the same ScriptOptions are used in both LoadScriptAsync and VerifyScriptAsync
        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(tempDir);
        var scriptPath = Path.Combine(tempDir, "build.csando");

        try
        {
            // Script that uses ANDO API should compile successfully
            await File.WriteAllTextAsync(scriptPath, """
                Ando.UseImage("mcr.microsoft.com/dotnet/sdk:9.0");
                var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
                Dotnet.Restore(project);
                Dotnet.Build(project);
                Npm.Install(Directory("./frontend"));
                """);

            var host = new ScriptHost(_logger);
            var errors = await host.VerifyScriptAsync(scriptPath);

            errors.ShouldBeEmpty();
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
