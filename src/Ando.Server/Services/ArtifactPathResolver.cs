// =============================================================================
// ArtifactPathResolver.cs
//
// Summary: Resolves artifact storage paths to safe absolute filesystem paths.
//
// Build artifacts are stored in the database as paths relative to the artifact
// root directory. This helper normalizes those paths and validates they remain
// inside the configured storage root.
// =============================================================================

namespace Ando.Server.Services;

public static class ArtifactPathResolver
{
    public static string ResolveAbsolutePath(string artifactsRoot, string storagePath)
    {
        if (string.IsNullOrWhiteSpace(storagePath))
        {
            return string.Empty;
        }

        return Path.IsPathRooted(storagePath)
            ? Path.GetFullPath(storagePath)
            : Path.GetFullPath(Path.Combine(artifactsRoot, storagePath));
    }

    public static bool IsWithinRoot(string artifactsRoot, string absolutePath)
    {
        var normalizedRoot = NormalizeRoot(artifactsRoot);
        var normalizedPath = Path.GetFullPath(absolutePath);

        return normalizedPath.StartsWith(normalizedRoot, StringComparison.Ordinal);
    }

    private static string NormalizeRoot(string artifactsRoot)
    {
        var normalizedRoot = Path.GetFullPath(artifactsRoot);

        if (!normalizedRoot.EndsWith(Path.DirectorySeparatorChar))
        {
            normalizedRoot += Path.DirectorySeparatorChar;
        }

        return normalizedRoot;
    }
}
