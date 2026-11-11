// =============================================================================
// FilesOperations.cs
//
// Summary: File operations that run in the build container.
//
// Files.Copy replaces ad-hoc `cp` calls in Shell.Bash with a typed step that
// has defined semantics for files, directories and missing sources.
//
// Design Decisions:
// - Paths are passed to bash as positional arguments, never interpolated.
// - Directory copies merge CONTENTS into the destination (cp -a src/. dst/) so
//   the result does not depend on whether the destination already exists.
// =============================================================================

using Ando.Context;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// File operations in the build container.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class FilesOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    // Positional arguments: $1 source, $2 destination, $3 optional (1 or 0).
    internal const string CopyScript = """
        set -euo pipefail
        src=$1; dst=$2; optional=$3
        if [ ! -e "$src" ] && [ ! -L "$src" ]; then
          if [ "$optional" = 1 ]; then echo "Files.Copy: $src does not exist; skipped (optional)."; exit 0; fi
          echo "Files.Copy: source not found: $src" >&2; exit 1
        fi
        if [ -d "$src" ]; then
          mkdir -p -- "$dst"
          cp -a -- "$src"/. "$dst"/
        else
          case "$dst" in
            */) mkdir -p -- "$dst"; cp -a -- "$src" "$dst" ;;
            *)
              if [ -d "$dst" ]; then cp -a -- "$src" "$dst"/
              else mkdir -p -- "$(dirname -- "$dst")"; cp -a -- "$src" "$dst"; fi ;;
          esac
        fi
        """;

    /// <summary>
    /// Registers a step that copies a file or directory in the build container. Semantics:
    /// - File source: if <paramref name="destination"/> ends with "/" or is an existing directory,
    ///   the file is copied INTO that directory under its own name (the directory is created for
    ///   a trailing "/"); otherwise <paramref name="destination"/> is the target file path
    ///   (overwritten if it exists) and its parent directories are created.
    /// - Directory source: the CONTENTS of the directory are copied recursively into
    ///   <paramref name="destination"/> (like `cp -a src/. dst/`). The destination is created if
    ///   missing; if it exists the trees merge and same-named files are overwritten. The source
    ///   directory name is never added (copying "a" to "b" gives "b/x", not "b/a/x").
    /// - Missing parent directories of the destination are created. Permissions, timestamps and
    ///   symlinks are preserved (cp -a).
    /// - A missing source fails the step, unless <paramref name="optional"/> is true: then the
    ///   step succeeds and logs that it was skipped.
    /// Relative paths are relative to /workspace.
    /// Usage: Files.Copy("config/prod.json", "artifacts/web/config.json");
    ///        Files.Copy("public", "artifacts/web", optional: true);
    /// </summary>
    /// <param name="source">File or directory to copy.</param>
    /// <param name="destination">Target file path, or target directory for a directory source.</param>
    /// <param name="optional">True to skip silently (info log) when the source does not exist.</param>
    public CommandStep Copy(string source, string destination, bool optional = false)
    {
        // Resolve() drops a trailing slash only for "."; keep the "copy into directory" marker.
        var dst = ContainerPaths.Resolve(destination);
        if (destination.EndsWith('/') && !dst.EndsWith('/'))
        {
            dst += "/";
        }
        return ShellCommandOperations.RegisterCommandStep(Registry, Logger, ExecutorFactory, "Files.Copy", "bash",
            ["-c", CopyScript, "ando-files-copy", ContainerPaths.Resolve(source), dst, optional ? "1" : "0"],
            $"{source} -> {destination}");
    }
}
