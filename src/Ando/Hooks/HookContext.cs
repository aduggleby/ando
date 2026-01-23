// =============================================================================
// HookContext.cs
//
// Summary: Provides context information to hooks via environment variables.
//
// HookContext carries information about the current command and its parameters.
// This information is exposed to hook scripts as environment variables:
// - ANDO_COMMAND: The command being executed (bump, commit, etc.)
// - ANDO_OLD_VERSION: Version before bump (bump hooks only)
// - ANDO_NEW_VERSION: Version after bump (post-bump hooks only)
// - ANDO_BUMP_TYPE: Type of bump (patch, minor, major)
// =============================================================================

namespace Ando.Hooks;

/// <summary>
/// Context information passed to hook scripts via environment variables.
/// </summary>
public record HookContext
{
    /// <summary>
    /// The command being executed (bump, commit, run, etc.).
    /// </summary>
    public string Command { get; init; } = "";

    /// <summary>
    /// The version before a bump operation (bump hooks only).
    /// </summary>
    public string? OldVersion { get; init; }

    /// <summary>
    /// The version after a bump operation (post-bump hooks only).
    /// </summary>
    public string? NewVersion { get; init; }

    /// <summary>
    /// The type of bump: patch, minor, or major (bump hooks only).
    /// </summary>
    public string? BumpType { get; init; }

    /// <summary>
    /// Converts the context to a dictionary of environment variables.
    /// </summary>
    public Dictionary<string, string> ToEnvironment()
    {
        var env = new Dictionary<string, string>
        {
            ["ANDO_COMMAND"] = Command
        };

        if (OldVersion != null)
            env["ANDO_OLD_VERSION"] = OldVersion;

        if (NewVersion != null)
            env["ANDO_NEW_VERSION"] = NewVersion;

        if (BumpType != null)
            env["ANDO_BUMP_TYPE"] = BumpType;

        return env;
    }
}
