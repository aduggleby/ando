// =============================================================================
// HookScriptHost.cs
//
// Summary: Roslyn scripting host for executing hook scripts.
//
// HookScriptHost uses Microsoft.CodeAnalysis.CSharp.Scripting to compile and
// execute hook scripts. Unlike the main ScriptHost, it uses HookGlobals which
// provides a simpler API suitable for hooks (Log, Env, Shell, Directory).
//
// Design Decisions:
// - Separate from ScriptHost to avoid coupling hooks to build infrastructure
// - Uses HookGlobals for simpler API surface
// - Sets environment variables from HookContext before execution
// - Restores original environment variables after execution
// - Has configurable timeout to prevent runaway hooks
// =============================================================================

using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;
using Ando.Logging;
using Ando.References;

namespace Ando.Hooks;

/// <summary>
/// Roslyn scripting host for executing hook scripts.
/// </summary>
public class HookScriptHost
{
    private readonly string _projectRoot;
    private readonly IBuildLogger _logger;
    private const int DefaultTimeoutMs = 300000; // 5 minutes

    public HookScriptHost(string projectRoot, IBuildLogger logger)
    {
        _projectRoot = projectRoot;
        _logger = logger;
    }

    /// <summary>
    /// Executes a hook script with the given environment variables.
    /// </summary>
    /// <param name="hookPath">Path to the hook script.</param>
    /// <param name="environment">Environment variables to set for the hook.</param>
    /// <param name="timeoutMs">Timeout in milliseconds.</param>
    public async Task ExecuteAsync(
        string hookPath,
        Dictionary<string, string> environment,
        int timeoutMs = DefaultTimeoutMs)
    {
        if (!File.Exists(hookPath))
        {
            throw new FileNotFoundException($"Hook script not found: {hookPath}", hookPath);
        }

        // Save and set environment variables.
        var originalValues = new Dictionary<string, string?>();
        foreach (var (key, value) in environment)
        {
            originalValues[key] = Environment.GetEnvironmentVariable(key);
            Environment.SetEnvironmentVariable(key, value);
        }

        try
        {
            var globals = new HookGlobals(_projectRoot, _logger);
            var scriptContent = await File.ReadAllTextAsync(hookPath);

            // Configure Roslyn with required assemblies and namespace imports.
            var options = ScriptOptions.Default
                .WithReferences(
                    typeof(HookGlobals).Assembly,       // ANDO types
                    typeof(object).Assembly,           // mscorlib
                    typeof(Console).Assembly,          // System.Console
                    typeof(File).Assembly,             // System.IO
                    typeof(Task).Assembly,             // System.Threading.Tasks
                    typeof(Enumerable).Assembly)       // System.Linq
                .WithImports(
                    "System",
                    "System.IO",
                    "System.Linq",
                    "System.Threading.Tasks",
                    "System.Collections.Generic",
                    "Ando.Context",
                    "Ando.Hooks",
                    "Ando.References");

            // Execute with timeout.
            using var cts = new CancellationTokenSource(timeoutMs);

            var task = CSharpScript.RunAsync(scriptContent, options, globals, typeof(HookGlobals));

            // Wait for completion or timeout.
            var completedTask = await Task.WhenAny(task, Task.Delay(timeoutMs, cts.Token));

            if (completedTask != task)
            {
                throw new TimeoutException(
                    $"Hook '{Path.GetFileName(hookPath)}' timed out after {timeoutMs / 1000} seconds");
            }

            // Propagate any exception from script execution.
            await task;
        }
        catch (CompilationErrorException ex)
        {
            var errors = string.Join(Environment.NewLine, ex.Diagnostics.Select(d => $"  {d}"));
            throw new HookAbortException(
                Path.GetFileName(hookPath),
                $"Hook compilation failed:{Environment.NewLine}{errors}");
        }
        finally
        {
            // Restore original environment variables.
            foreach (var (key, originalValue) in originalValues)
            {
                Environment.SetEnvironmentVariable(key, originalValue);
            }
        }
    }
}
