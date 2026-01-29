// =============================================================================
// DocfxOperations.cs
//
// Summary: Provides DocFX operations for generating API documentation from
// C# XML documentation comments.
//
// DocFX is Microsoft's documentation generator for .NET projects. It reads
// XML documentation comments from source code and generates static HTML
// documentation sites.
//
// Architecture:
// - Methods register steps for deferred execution
// - Install() checks if DocFX is installed and installs if needed
// - GenerateDocs() runs docfx metadata and build commands
// - CopyToDirectory() copies the generated docs to a target directory
//
// Example usage:
//   Docfx.Install();
//   Docfx.GenerateDocs("./docfx.json");
//   Docfx.CopyToDirectory("./website/public/apidocs");
//
// Design Decisions:
// - DocFX is installed as a dotnet global tool for consistency
// - GenerateDocs runs both metadata and build in a single step
// - Cleanup of intermediate files is handled automatically
// =============================================================================

using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// DocFX operations for generating API documentation from C# XML comments.
/// </summary>
public class DocfxOperations(
    StepRegistry registry,
    IBuildLogger logger,
    Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    /// <summary>
    /// Checks if DocFX is installed as a dotnet global tool.
    /// </summary>
    /// <returns>True if DocFX is available, false otherwise.</returns>
    public bool IsInstalled()
    {
        var executor = ExecutorFactory();
        var result = executor.ExecuteAsync("dotnet", ["tool", "list", "-g"]).GetAwaiter().GetResult();

        if (!result.Success || result.Output == null)
        {
            return false;
        }

        // Check if docfx is in the output
        return result.Output.Contains("docfx", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Installs DocFX as a dotnet global tool if not already installed.
    /// </summary>
    public void Install()
    {
        Registry.Register("Docfx.Install", async () =>
        {
            var executor = ExecutorFactory();

            // Check if already installed
            var listResult = await executor.ExecuteAsync("dotnet", ["tool", "list", "-g"]);
            if (listResult.Success && listResult.Output?.Contains("docfx", StringComparison.OrdinalIgnoreCase) == true)
            {
                Logger.Info("DocFX is already installed");
                return true;
            }

            // Install docfx
            Logger.Info("Installing DocFX...");
            var installResult = await executor.ExecuteAsync("dotnet", ["tool", "install", "-g", "docfx"]);
            return installResult.Success;
        });
    }

    /// <summary>
    /// Generates API documentation from a docfx.json configuration file.
    /// Runs both 'docfx metadata' to extract API metadata and 'docfx build' to generate HTML.
    /// </summary>
    /// <param name="configPath">Path to the docfx.json configuration file. Defaults to "./docfx.json".</param>
    public void GenerateDocs(string configPath = "./docfx.json")
    {
        Registry.Register("Docfx.GenerateDocs", async () =>
        {
            var executor = ExecutorFactory();

            // Run docfx metadata to extract API documentation from source
            Logger.Info("Extracting API metadata...");
            var metadataResult = await executor.ExecuteAsync("docfx", ["metadata", configPath]);
            if (!metadataResult.Success)
            {
                Logger.Error("Failed to extract API metadata");
                return false;
            }

            // Run docfx build to generate the HTML documentation
            Logger.Info("Building documentation site...");
            var buildResult = await executor.ExecuteAsync("docfx", ["build", configPath]);
            if (!buildResult.Success)
            {
                Logger.Error("Failed to build documentation");
                return false;
            }

            Logger.Info("Documentation generated successfully");
            return true;
        }, configPath);
    }

    /// <summary>
    /// Copies generated documentation from the DocFX output directory to a target directory.
    /// </summary>
    /// <param name="sourceDir">Source directory containing generated docs (e.g., "_apidocs").</param>
    /// <param name="targetDir">Target directory to copy docs to (e.g., "./website/public/apidocs").</param>
    public void CopyToDirectory(string sourceDir, string targetDir)
    {
        Registry.Register("Docfx.CopyToDirectory", async () =>
        {
            var executor = ExecutorFactory();

            // Remove existing target directory
            Logger.Info($"Copying documentation to {targetDir}...");
            var rmResult = await executor.ExecuteAsync("rm", ["-rf", targetDir]);
            if (!rmResult.Success)
            {
                Logger.Warning($"Could not remove existing directory: {targetDir}");
            }

            // Copy source to target
            var cpResult = await executor.ExecuteAsync("cp", ["-r", sourceDir, targetDir]);
            if (!cpResult.Success)
            {
                Logger.Error($"Failed to copy documentation to {targetDir}");
                return false;
            }

            Logger.Info($"Documentation copied to {targetDir}");
            return true;
        }, $"{sourceDir} -> {targetDir}");
    }

    /// <summary>
    /// Cleans up intermediate DocFX files (api/ and output directories).
    /// </summary>
    /// <param name="outputDir">The DocFX output directory to remove. Defaults to "_apidocs".</param>
    public void Cleanup(string outputDir = "_apidocs")
    {
        Registry.Register("Docfx.Cleanup", async () =>
        {
            var executor = ExecutorFactory();

            // Remove api directory (generated metadata)
            Logger.Info("Cleaning up DocFX intermediate files...");
            await executor.ExecuteAsync("rm", ["-rf", "api"]);

            // Remove output directory
            await executor.ExecuteAsync("rm", ["-rf", outputDir]);

            Logger.Info("DocFX cleanup complete");
            return true;
        });
    }

    /// <summary>
    /// Generates documentation and copies it to the target directory in one step.
    /// This is a convenience method that combines GenerateDocs, CopyToDirectory, and Cleanup.
    /// Creates a redirect index.html that points to the API namespace page.
    /// </summary>
    /// <param name="configPath">Path to the docfx.json configuration file.</param>
    /// <param name="outputDir">DocFX output directory (from docfx.json).</param>
    /// <param name="targetDir">Target directory to copy the final documentation to.</param>
    public void BuildAndCopy(string configPath, string outputDir, string targetDir)
    {
        Registry.Register("Docfx.BuildAndCopy", async () =>
        {
            var executor = ExecutorFactory();

            // Run docfx metadata
            Logger.Info("Extracting API metadata...");
            var metadataResult = await executor.ExecuteAsync("docfx", ["metadata", configPath]);
            if (!metadataResult.Success)
            {
                Logger.Error("Failed to extract API metadata");
                return false;
            }

            // Run docfx build
            Logger.Info("Building documentation site...");
            var buildResult = await executor.ExecuteAsync("docfx", ["build", configPath]);
            if (!buildResult.Success)
            {
                Logger.Error("Failed to build documentation");
                return false;
            }

            // Copy to target directory
            Logger.Info($"Copying documentation to {targetDir}...");
            await executor.ExecuteAsync("rm", ["-rf", targetDir]);
            var cpResult = await executor.ExecuteAsync("cp", ["-r", outputDir, targetDir]);
            if (!cpResult.Success)
            {
                Logger.Error($"Failed to copy documentation to {targetDir}");
                return false;
            }

            // Cleanup intermediate files
            Logger.Info("Cleaning up intermediate files...");
            await executor.ExecuteAsync("rm", ["-rf", "api"]);
            await executor.ExecuteAsync("rm", ["-rf", outputDir]);

            Logger.Info($"Documentation generated and copied to {targetDir}");
            return true;
        }, $"{configPath} -> {targetDir}");
    }
}
