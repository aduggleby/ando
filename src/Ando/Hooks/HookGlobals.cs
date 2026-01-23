// =============================================================================
// HookGlobals.cs
//
// Summary: Global variables exposed to hook scripts.
//
// HookGlobals provides a subset of the ScriptGlobals API for hook scripts.
// Hooks run on the host machine (not in Docker) and need access to:
// - Log: Logging operations
// - Env: Environment variable access
// - Root: Project root path
// - Directory: Directory reference creation
// - Shell: Command execution
//
// Design Decisions:
// - Simpler than ScriptGlobals since hooks don't need build operations
// - Reuses LogOperations for consistent logging
// - Provides Shell for running arbitrary commands
// - Root is a string path (not BuildPath) for simplicity
// =============================================================================

using Ando.Context;
using Ando.Logging;
using Ando.Operations;
using Ando.References;

namespace Ando.Hooks;

/// <summary>
/// Global variables exposed to hook scripts.
/// Provides a simpler API than ScriptGlobals for hook-specific operations.
/// </summary>
public class HookGlobals
{
    /// <summary>
    /// Project root directory (where build.csando is located).
    /// </summary>
    public string Root { get; }

    /// <summary>
    /// Logging operations for hook output.
    /// Usage: Log.Info("message"), Log.Warning("message"), Log.Error("message")
    /// </summary>
    public HookLogOperations Log { get; }

    /// <summary>
    /// Shell command execution.
    /// Usage: var result = await Shell.RunAsync("dotnet", "test");
    /// </summary>
    public ShellOperations Shell { get; }

    /// <summary>
    /// Creates a directory reference from a path.
    /// Usage: var frontend = Directory("./frontend");
    /// </summary>
    public DirectoryRef Directory(string path = ".") => new DirectoryRef(path);

    /// <summary>
    /// Gets an environment variable value.
    /// By default, throws if the variable is not set. Pass required: false to return null.
    /// Usage: var apiKey = Env("API_KEY");
    /// Usage: var optional = Env("OPTIONAL_VAR", required: false);
    /// </summary>
    /// <param name="name">Environment variable name.</param>
    /// <param name="required">If true (default), throws when variable is not set.</param>
    /// <returns>The environment variable value, or null if not set and required is false.</returns>
    public string? Env(string name, bool required = true)
    {
        var value = Environment.GetEnvironmentVariable(name);

        if (required && string.IsNullOrEmpty(value))
        {
            throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
        }

        return value;
    }

    public HookGlobals(string projectRoot, IBuildLogger logger)
    {
        Root = projectRoot;
        Log = new HookLogOperations(logger);
        Shell = new ShellOperations(projectRoot);
    }
}

/// <summary>
/// Simplified logging operations for hooks.
/// Delegates to IBuildLogger but doesn't require full build context.
/// </summary>
public class HookLogOperations
{
    private readonly IBuildLogger _logger;

    public HookLogOperations(IBuildLogger logger)
    {
        _logger = logger;
    }

    public void Info(string message) => _logger.Info($"  {message}");
    public void Warning(string message) => _logger.Warning($"  {message}");
    public void Error(string message) => _logger.Error($"  {message}");
    public void Debug(string message) => _logger.Debug($"  {message}");
}
