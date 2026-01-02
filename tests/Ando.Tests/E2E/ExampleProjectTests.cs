using Ando.Cli;
using System.Collections;

namespace Ando.Tests.E2E;

/// <summary>
/// End-to-end tests that run example projects.
/// These tests execute actual build.ando scripts using --local mode.
/// Tests dynamically discover and run all examples in the examples/ directory.
/// </summary>
[Trait("Category", "E2E")]
public class ExampleProjectTests : IDisposable
{
    private readonly string _originalDirectory;
    private readonly string _examplesRoot;

    public ExampleProjectTests()
    {
        _originalDirectory = Environment.CurrentDirectory;
        _examplesRoot = GetExamplesRoot();
    }

    public void Dispose()
    {
        Environment.CurrentDirectory = _originalDirectory;

        // Clean up generated artifacts in all example directories
        foreach (var exampleDir in Directory.GetDirectories(_examplesRoot))
        {
            CleanupExampleArtifacts(exampleDir);
        }
    }

    private static string GetExamplesRoot()
    {
        // Find the examples directory relative to the test assembly location
        var assemblyLocation = typeof(ExampleProjectTests).Assembly.Location;
        var testDir = Path.GetDirectoryName(assemblyLocation)!;

        // Navigate from bin/Debug/net9.0 up to the repo root
        var repoRoot = Path.GetFullPath(Path.Combine(testDir, "..", "..", "..", "..", ".."));
        var examplesRoot = Path.Combine(repoRoot, "examples");

        if (!Directory.Exists(examplesRoot))
        {
            throw new InvalidOperationException($"Examples directory not found at: {examplesRoot}");
        }

        return examplesRoot;
    }

    private static void CleanupExampleArtifacts(string exampleDir)
    {
        TryDeleteDirectory(Path.Combine(exampleDir, "dist"));
        TryDeleteDirectory(Path.Combine(exampleDir, "artifacts"));
    }

    private static void TryDeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                // Ignore cleanup errors
            }
        }
    }

    /// <summary>
    /// Provides all example project directories as test data.
    /// </summary>
    public class ExampleProjectData : IEnumerable<object[]>
    {
        public IEnumerator<object[]> GetEnumerator()
        {
            var examplesRoot = GetExamplesRoot();

            foreach (var dir in Directory.GetDirectories(examplesRoot))
            {
                var buildScript = Path.Combine(dir, "build.ando");
                if (File.Exists(buildScript))
                {
                    // Return just the directory name for cleaner test names
                    yield return new object[] { Path.GetFileName(dir) };
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [Theory]
    [ClassData(typeof(ExampleProjectData))]
    public async Task Example_BuildsSuccessfully(string exampleName)
    {
        var projectDir = Path.Combine(_examplesRoot, exampleName);
        Environment.CurrentDirectory = projectDir;

        var cli = new AndoCli(new[] { "run", "--local" });
        var exitCode = await cli.RunAsync();

        exitCode.ShouldBe(0, $"Example '{exampleName}' failed to build");
    }

    [Theory]
    [ClassData(typeof(ExampleProjectData))]
    public async Task Example_CreatesDistDirectory(string exampleName)
    {
        var projectDir = Path.Combine(_examplesRoot, exampleName);
        Environment.CurrentDirectory = projectDir;

        var cli = new AndoCli(new[] { "run", "--local" });
        var exitCode = await cli.RunAsync();

        exitCode.ShouldBe(0, $"Example '{exampleName}' failed to build");

        // Verify the dist directory was created if publish is used
        var buildScript = await File.ReadAllTextAsync(Path.Combine(projectDir, "build.ando"));
        if (buildScript.Contains("Dotnet.Publish"))
        {
            var distDir = Path.Combine(projectDir, "dist");
            Directory.Exists(distDir).ShouldBeTrue($"Example '{exampleName}' should create dist directory");
        }
    }

    [Fact]
    public async Task NoBuildScript_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ando-e2e-empty-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;

            var cli = new AndoCli(new[] { "run", "--local" });
            var exitCode = await cli.RunAsync();

            exitCode.ShouldNotBe(0);
        }
        finally
        {
            Environment.CurrentDirectory = _originalDirectory;
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task InvalidBuildScript_ReturnsError()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"ando-e2e-invalid-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "build.ando"),
                """
                this is not valid C# syntax !!!
                """);

            Environment.CurrentDirectory = tempDir;

            var cli = new AndoCli(new[] { "run", "--local" });
            var exitCode = await cli.RunAsync();

            exitCode.ShouldNotBe(0);
        }
        finally
        {
            Environment.CurrentDirectory = _originalDirectory;
            TryDeleteDirectory(tempDir);
        }
    }

    [Fact]
    public async Task CleanCommand_RemovesArtifacts()
    {
        // Use the first available example project
        var firstExample = Directory.GetDirectories(_examplesRoot)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "build.ando")));

        if (firstExample == null)
        {
            throw new InvalidOperationException("No example projects found");
        }

        // Create artifacts directory
        var artifactsDir = Path.Combine(firstExample, "artifacts");
        Directory.CreateDirectory(artifactsDir);
        await File.WriteAllTextAsync(Path.Combine(artifactsDir, "test.txt"), "test");

        Environment.CurrentDirectory = firstExample;

        var cli = new AndoCli(new[] { "clean", "--artifacts" });
        var exitCode = await cli.RunAsync();

        exitCode.ShouldBe(0);
        Directory.Exists(artifactsDir).ShouldBeFalse();
    }
}
