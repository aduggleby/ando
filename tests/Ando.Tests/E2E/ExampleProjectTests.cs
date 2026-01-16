// =============================================================================
// ExampleProjectTests.cs
//
// Summary: End-to-end tests that run example projects.
//
// Tests execute actual build.csando scripts in Docker containers.
// Dynamically discovers and runs all examples in the examples/ directory.
// These tests are skipped if Docker is not available.
// =============================================================================

using Ando.Cli;
using Ando.Execution;
using Ando.Tests.TestFixtures;
using System.Collections;

namespace Ando.Tests.E2E;
[Trait("Category", "E2E")]
public class ExampleProjectTests : IDisposable
{
    private readonly string _originalDirectory;
    private readonly string _examplesRoot;
    private static readonly bool _dockerAvailable = CheckDockerAvailability();

    public ExampleProjectTests()
    {
        _originalDirectory = Environment.CurrentDirectory;
        _examplesRoot = GetExamplesRoot();
    }

    private static bool CheckDockerAvailability()
    {
        var dockerManager = new DockerManager(new TestLogger());
        return dockerManager.IsDockerAvailable();
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
    /// Provides example project directories that can be tested with the default Docker image.
    /// Excludes examples that require special tooling (npm, Azure CLI, etc.).
    /// </summary>
    public class ExampleProjectData : IEnumerable<object[]>
    {
        // Examples that require special tooling not in the default Docker image
        private static readonly HashSet<string> ExcludedExamples = new(StringComparer.OrdinalIgnoreCase)
        {
            "0004-NodeProject",    // Requires npm/Node.js
            "0005-EfMigrations",   // Requires dotnet-ef tool
            "0007-AzureBicep",     // Requires Azure CLI
            "0008-AzureFullStack"  // Requires Azure CLI
        };

        public IEnumerator<object[]> GetEnumerator()
        {
            var examplesRoot = GetExamplesRoot();

            foreach (var dir in Directory.GetDirectories(examplesRoot))
            {
                var buildScript = Path.Combine(dir, "build.csando");
                var dirName = Path.GetFileName(dir);

                if (File.Exists(buildScript) && !ExcludedExamples.Contains(dirName))
                {
                    // Return just the directory name for cleaner test names
                    yield return new object[] { dirName };
                }
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    [SkippableTheory]
    [ClassData(typeof(ExampleProjectData))]
    public async Task Example_BuildsSuccessfully(string exampleName)
    {
        Skip.IfNot(_dockerAvailable, "Docker is not available");

        var projectDir = Path.Combine(_examplesRoot, exampleName);
        Environment.CurrentDirectory = projectDir;

        var cli = new AndoCli(new[] { "run" });
        var exitCode = await cli.RunAsync();

        exitCode.ShouldBe(0, $"Example '{exampleName}' failed to build");
    }

    [SkippableTheory]
    [ClassData(typeof(ExampleProjectData))]
    public async Task Example_CreatesDistDirectory(string exampleName)
    {
        Skip.IfNot(_dockerAvailable, "Docker is not available");

        var projectDir = Path.Combine(_examplesRoot, exampleName);
        Environment.CurrentDirectory = projectDir;

        var cli = new AndoCli(new[] { "run" });
        var exitCode = await cli.RunAsync();

        exitCode.ShouldBe(0, $"Example '{exampleName}' failed to build");

        // Verify the dist directory was created if publish is used
        var buildScript = await File.ReadAllTextAsync(Path.Combine(projectDir, "build.csando"));
        if (buildScript.Contains("Dotnet.Publish"))
        {
            var distDir = Path.Combine(projectDir, "dist");
            Directory.Exists(distDir).ShouldBeTrue($"Example '{exampleName}' should create dist directory");
        }
    }

    [SkippableFact]
    public async Task NoBuildScript_ReturnsError()
    {
        Skip.IfNot(_dockerAvailable, "Docker is not available");

        var tempDir = Path.Combine(Path.GetTempPath(), $"ando-e2e-empty-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            Environment.CurrentDirectory = tempDir;

            var cli = new AndoCli(new[] { "run" });
            var exitCode = await cli.RunAsync();

            exitCode.ShouldNotBe(0);
        }
        finally
        {
            Environment.CurrentDirectory = _originalDirectory;
            TryDeleteDirectory(tempDir);
        }
    }

    [SkippableFact]
    public async Task InvalidBuildScript_ReturnsError()
    {
        Skip.IfNot(_dockerAvailable, "Docker is not available");

        var tempDir = Path.Combine(Path.GetTempPath(), $"ando-e2e-invalid-{Guid.NewGuid()}");
        Directory.CreateDirectory(tempDir);

        try
        {
            await File.WriteAllTextAsync(
                Path.Combine(tempDir, "build.csando"),
                """
                this is not valid C# syntax !!!
                """);

            Environment.CurrentDirectory = tempDir;

            var cli = new AndoCli(new[] { "run" });
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
        // This test doesn't require Docker - it just tests the clean command
        var firstExample = Directory.GetDirectories(_examplesRoot)
            .FirstOrDefault(d => File.Exists(Path.Combine(d, "build.csando")));

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
