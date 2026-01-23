// =============================================================================
// HookAbortException.cs
//
// Summary: Exception thrown when a hook requests to abort the current command.
//
// When a hook script exits with a non-zero code or throws an exception,
// HookAbortException is used to signal that the command should not proceed.
// Pre-hooks use this to prevent command execution; post-hooks only warn.
// =============================================================================

namespace Ando.Hooks;

/// <summary>
/// Exception thrown when a hook requests to abort the current command.
/// </summary>
public class HookAbortException : Exception
{
    /// <summary>
    /// The name of the hook that caused the abort.
    /// </summary>
    public string HookName { get; }

    /// <summary>
    /// The exit code from the hook script.
    /// </summary>
    public int ExitCode { get; }

    public HookAbortException(string hookName, int exitCode, string message)
        : base(message)
    {
        HookName = hookName;
        ExitCode = exitCode;
    }

    public HookAbortException(string hookName, string message)
        : this(hookName, 1, message)
    {
    }
}
