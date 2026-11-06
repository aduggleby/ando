// =============================================================================
// ContainerPaths.cs
//
// Summary: Resolves script paths to paths inside the build container.
//
// Relative paths are relative to /workspace. Only a leading "./" is removed, so
// hidden directories such as ".run" keep their name.
// =============================================================================

namespace Ando.Context;

/// <summary>Resolves paths to absolute paths in the build container.</summary>
public static class ContainerPaths
{
    /// <summary>The project root inside the build container.</summary>
    public const string Workspace = "/workspace";

    /// <summary>
    /// Returns <paramref name="path"/> as an absolute container path: absolute
    /// paths are kept, relative ones are resolved against /workspace.
    /// </summary>
    public static string Resolve(string path)
    {
        var normalized = path.Replace('\\', '/');
        if (normalized.StartsWith('/'))
        {
            return normalized;
        }

        while (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        if (normalized is "." or "")
        {
            return Workspace;
        }

        return $"{Workspace}/{normalized}";
    }
}
