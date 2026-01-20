// =============================================================================
// ScriptHost.cs
//
// Summary: Roslyn-based script host for loading and executing build.csando files.
//
// ScriptHost uses Microsoft.CodeAnalysis.CSharp.Scripting (Roslyn) to compile
// and execute build scripts at runtime. Scripts are real C# code with access
// to the ANDO API via global variables.
//
// Architecture:
// - Creates a BuildContext with all operations and step registry
// - Creates ScriptGlobals which exposes ANDO API as global variables
// - Configures Roslyn with required assemblies and namespace imports
// - Executes script which registers steps (doesn't run them yet)
// - Returns the context for WorkflowRunner to execute registered steps
//
// Design Decisions:
// - Roslyn scripting allows real C# with IntelliSense in compatible editors
// - Pre-imported namespaces reduce boilerplate in build scripts
// - Script execution registers steps but doesn't run them (lazy evaluation)
// - Compilation errors are caught and reported with helpful diagnostics
// =============================================================================

using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Ando.Logging;

namespace Ando.Scripting;

/// <summary>
/// Roslyn-based script host for loading and executing build.csando files.
/// Scripts are C# code that registers build steps for later execution.
/// </summary>
public class ScriptHost(IBuildLogger logger)
{
    // Active profiles from CLI, set before loading script.
    private List<string> _activeProfiles = [];

    /// <summary>
    /// Sets the active profiles from CLI arguments.
    /// Call this before LoadScriptAsync to ensure profiles are available.
    /// </summary>
    /// <param name="profiles">Profile names from CLI (e.g., from -p push,release).</param>
    public void SetActiveProfiles(IEnumerable<string> profiles)
    {
        _activeProfiles = profiles.ToList();
    }

    /// <summary>
    /// Loads and executes a build script, returning the configured build context.
    /// The script registers steps in the StepRegistry but doesn't execute them.
    /// </summary>
    /// <param name="scriptPath">Path to the build.csando file.</param>
    /// <param name="rootPath">Project root directory.</param>
    /// <returns>BuildContext with registered steps ready for execution.</returns>
    public async Task<BuildContext> LoadScriptAsync(string scriptPath, string rootPath)
    {
        if (!File.Exists(scriptPath))
        {
            throw new FileNotFoundException($"Build script not found: {scriptPath}", scriptPath);
        }

        // Create the build context that holds all state and operations.
        var context = new BuildContext(rootPath, logger);

        // Set active profiles before script execution so DefineProfile can check state.
        context.ProfileRegistry.SetActiveProfiles(_activeProfiles);

        // ScriptGlobals exposes ANDO API as global variables in the script.
        var globals = new ScriptGlobals(context);
        var scriptContent = await File.ReadAllTextAsync(scriptPath);

        // Configure Roslyn with required assemblies and namespace imports.
        // This allows scripts to use ANDO types without explicit using statements.
        var options = ScriptOptions.Default
            .WithReferences(
                typeof(BuildContext).Assembly,  // ANDO types
                typeof(object).Assembly,        // mscorlib/System.Private.CoreLib
                typeof(Console).Assembly,       // System.Console
                typeof(File).Assembly,          // System.IO
                typeof(Task).Assembly,          // System.Threading.Tasks
                typeof(Enumerable).Assembly)    // System.Linq
            .WithImports(
                "System",
                "System.IO",
                "System.Linq",
                "System.Threading.Tasks",
                "System.Collections.Generic",
                "Ando.Context",
                "Ando.Profiles",
                "Ando.References",
                "Ando.Operations",
                "Ando.Workflow",
                "Ando.Steps");

        try
        {
            // Execute the script. This runs the C# code which registers steps
            // in the StepRegistry. Steps are not executed yet - that happens
            // in WorkflowRunner after script loading completes.
            await CSharpScript.RunAsync(scriptContent, options, globals, typeof(ScriptGlobals));
        }
        catch (CompilationErrorException ex)
        {
            // Report compilation errors with helpful diagnostics.
            logger.Error("Script compilation failed:");
            foreach (var diagnostic in ex.Diagnostics)
            {
                logger.Error($"  {diagnostic}");
            }
            throw;
        }

        return context;
    }

    /// <summary>
    /// Verifies a build script compiles without errors, without executing it.
    /// Returns compilation diagnostics if any errors are found.
    /// </summary>
    /// <param name="scriptPath">Path to the build.csando file.</param>
    /// <returns>List of compilation error messages, empty if script is valid.</returns>
    public async Task<List<string>> VerifyScriptAsync(string scriptPath)
    {
        var errors = new List<string>();

        if (!File.Exists(scriptPath))
        {
            errors.Add($"Build script not found: {scriptPath}");
            return errors;
        }

        var scriptContent = await File.ReadAllTextAsync(scriptPath);

        // Configure Roslyn with the same options used for actual execution.
        var options = ScriptOptions.Default
            .WithReferences(
                typeof(BuildContext).Assembly,
                typeof(object).Assembly,
                typeof(Console).Assembly,
                typeof(File).Assembly,
                typeof(Task).Assembly,
                typeof(Enumerable).Assembly)
            .WithImports(
                "System",
                "System.IO",
                "System.Linq",
                "System.Threading.Tasks",
                "System.Collections.Generic",
                "Ando.Context",
                "Ando.Profiles",
                "Ando.References",
                "Ando.Operations",
                "Ando.Workflow",
                "Ando.Steps");

        // Create and compile the script without running it.
        var script = CSharpScript.Create(scriptContent, options, typeof(ScriptGlobals));
        var diagnostics = script.Compile();

        // Collect any errors (warnings are ignored).
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Severity == Microsoft.CodeAnalysis.DiagnosticSeverity.Error)
            {
                errors.Add(diagnostic.ToString());
            }
        }

        return errors;
    }
}
