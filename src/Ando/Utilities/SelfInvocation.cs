// =============================================================================
// SelfInvocation.cs
//
// Summary: How to start the CLI that is running now.
//
// Ando.Build and Ando.StepTree start a child `ando`. Looking `ando` up on PATH
// can find another version than the running one (for example `./ando-dev` runs
// from source while PATH has an older tool), so children start the running CLI.
//
// Design Decisions:
// - Pure decision function with injected inputs, so it is unit-testable
// - Under the `dotnet` host (dotnet run, dotnet ando.dll) the entry assembly
//   is started with `dotnet <dll>`; otherwise the process path is the CLI
// - Falls back to `ando` when neither is known
// =============================================================================

namespace Ando.Utilities;

/// <summary>The command and leading arguments that start the current CLI.</summary>
public sealed record SelfInvocation(string Command, string[] PrefixArgs)
{
    /// <summary>Starts `ando` from PATH.</summary>
    public static SelfInvocation Fallback { get; } = new("ando", []);

    /// <summary>How to start the CLI that is running now.</summary>
    public static SelfInvocation Current { get; } =
        Resolve(Environment.ProcessPath, System.Reflection.Assembly.GetEntryAssembly()?.Location);

    /// <summary>Decides the invocation from the process path and the entry assembly path (either may be empty).</summary>
    public static SelfInvocation Resolve(string? processPath, string? entryAssemblyPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return Fallback;
        }

        var host = Path.GetFileNameWithoutExtension(processPath);
        if (!host.Equals("dotnet", StringComparison.OrdinalIgnoreCase))
        {
            return new SelfInvocation(processPath, []);
        }

        return string.IsNullOrWhiteSpace(entryAssemblyPath)
            ? Fallback
            : new SelfInvocation(processPath, [entryAssemblyPath]);
    }

    /// <summary>The arguments for starting the CLI with <paramref name="args"/>.</summary>
    public string[] WithArgs(IEnumerable<string> args) => [.. PrefixArgs, .. args];
}
