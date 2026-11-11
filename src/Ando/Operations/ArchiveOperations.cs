// =============================================================================
// ArchiveOperations.cs
//
// Summary: Deterministic zip and tar.gz archives for build scripts.
//
// Archive.Zip and Archive.TarGz register steps that run in the build container
// and produce byte-identical output for the same tree, regardless of file
// modification times, creation order, owners or the machine.
//
// How determinism is achieved (one shared bash script, arguments passed
// positionally so paths never need quoting):
// - The source tree is copied to a temporary staging directory, so the source
//   timestamps and permissions are never touched.
// - In the stage: directories are 755, files are 755 when any execute bit is
//   set and 644 otherwise, and every entry's mtime is set to the given epoch.
// - Entries are listed with `LC_ALL=C sort` (byte order of the relative path)
//   and archived in exactly that order, directories included (so empty
//   directories survive); there is no "./" prefix and no root entry.
// - zip: -X (no extra fields such as uid/gid or extended timestamps), TZ=UTC
//   (zip stores local DOS time), fixed -N level. tar: GNU format, --sort=name,
//   --owner=0 --group=0 --numeric-owner, --mtime, then gzip -n (no name or
//   mtime in the header) at a fixed level.
// - Symlinks, special files and file names containing a newline are rejected
//   with a clear message.
//
// Design Decisions:
// - Steps return CommandStep; Value is the SHA-256 (hex) of the archive.
// - zip/tar/gzip are installed with apt-get when missing (Debian/Ubuntu images).
// - Byte-identity holds for the same tool versions (same image); different
//   zip or gzip builds may legitimately compress differently.
// =============================================================================

using Ando.Context;
using Ando.Execution;
using Ando.Logging;
using Ando.Steps;

namespace Ando.Operations;

/// <summary>
/// Deterministic archives. Steps run in the build container.
/// Methods register steps in the workflow rather than executing immediately.
/// </summary>
public class ArchiveOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    : OperationsBase(registry, logger, executorFactory)
{
    // Positional arguments: $1 source dir, $2 output, $3 epoch, $4 level, $5 kind (zip|targz).
    internal const string Script = """
        set -euo pipefail
        export LC_ALL=C TZ=UTC
        src=$1; out=$2; epoch=$3; level=$4; kind=$5

        ensure() { # tool, apt package
          command -v "$1" >/dev/null 2>&1 && return 0
          command -v apt-get >/dev/null 2>&1 || { echo "Archive: $1 is not installed and apt-get is not available (only Debian and Ubuntu images are supported)." >&2; exit 1; }
          export DEBIAN_FRONTEND=noninteractive
          { apt-get update -qq && apt-get install -y -qq "$2"; } >&2
          command -v "$1" >/dev/null 2>&1 || { echo "Archive: could not install $1." >&2; exit 1; }
        }
        ensure find findutils; ensure sort coreutils; ensure sha256sum coreutils
        if [ "$kind" = zip ]; then ensure zip zip; else ensure tar tar; ensure gzip gzip; fi

        [ -d "$src" ] || { echo "Archive: source directory not found: $src" >&2; exit 1; }
        src=$(cd "$src" && pwd -P)
        case "$out" in "$src"/*) echo "Archive: the output $out must not be inside the source directory $src." >&2; exit 1;; esac

        links=$(find "$src" -type l | head -n 5)
        if [ -n "$links" ]; then
          echo "Archive: symlinks are not supported (archives must be self-contained and reproducible). Found:" >&2
          echo "$links" >&2; exit 1
        fi
        special=$(find "$src" ! -type f ! -type d | head -n 5)
        if [ -n "$special" ]; then echo "Archive: only regular files and directories are supported. Found:" >&2; echo "$special" >&2; exit 1; fi
        if [ -n "$(find "$src" -name $'*\n*' -print -quit)" ]; then echo "Archive: file names containing a newline are not supported." >&2; exit 1; fi

        work=$(mktemp -d)
        trap 'rm -rf "$work"' EXIT
        stage=$work/stage; mkdir "$stage"
        cp -a "$src"/. "$stage"/
        [ -n "$(ls -A "$stage")" ] || { echo "Archive: the source directory is empty: $src" >&2; exit 1; }
        find "$stage" -type d -exec chmod 755 {} +
        find "$stage" -type f -perm /111 -exec chmod 755 {} +
        find "$stage" -type f ! -perm /111 -exec chmod 644 {} +
        find "$stage" -exec touch -h -d "@$epoch" {} +

        (cd "$stage" && find . -mindepth 1 | sed 's|^\./||' | sort) > "$work/list"

        mkdir -p "$(dirname -- "$out")"
        rm -f -- "$out"
        if [ "$kind" = zip ]; then
          (cd "$stage" && zip -X -q "-$level" "$out" -@ < "$work/list") >&2
        else
          (cd "$stage" && tar --format=gnu --sort=name --no-recursion --owner=0 --group=0 --numeric-owner \
              --mtime="@$epoch" -cf - -T "$work/list") | gzip -n "-$level" > "$out"
        fi
        sha256sum -- "$out" | cut -d' ' -f1
        """;

    /// <summary>
    /// Registers a step that creates a deterministic zip of the contents of
    /// <paramref name="sourceDir"/> (no top-level directory in the archive). The same tree
    /// always yields the same bytes: entries sorted by path (byte order), every mtime set to
    /// the option timestamp (default 1980-01-01, zip minimum 315532800; 2 second resolution),
    /// no extra fields (zip -X), no uid/gid, directories stored as 755 entries, files 644 (755
    /// when executable), deflate level 6. Symlinks and special files fail the step. The source
    /// tree is copied to a staging directory first and never modified. Runs in the build
    /// container; zip is installed with apt-get when missing. Value is the archive's SHA-256.
    /// Relative paths are relative to /workspace. An existing file at the output path is replaced.
    /// Usage: Archive.Zip("artifacts/web", "artifacts/web.zip", o => o.WithTimestamp(Git.CommitTime));
    /// </summary>
    /// <param name="sourceDir">Directory whose contents are archived.</param>
    /// <param name="zipPath">Output zip path (must be outside sourceDir).</param>
    /// <param name="configure">Optional timestamp and compression level.</param>
    public CommandStep Zip(string sourceDir, string zipPath, Action<ArchiveOptions>? configure = null)
    {
        var options = new ArchiveOptions();
        configure?.Invoke(options);
        if (options.Timestamp < ArchiveOptions.DefaultTimestamp)
        {
            throw new ArgumentOutOfRangeException(nameof(configure),
                "A zip file cannot store times before 1980-01-01 (315532800); use WithTimestamp(315532800) or later.");
        }
        return Register("Archive.Zip", "zip", sourceDir, zipPath, options);
    }

    /// <summary>
    /// Registers a step that creates a deterministic .tar.gz of the contents of
    /// <paramref name="sourceDir"/> (no top-level directory in the archive). The same tree
    /// always yields the same bytes: GNU tar with --sort=name order (byte order of the relative
    /// path), owner and group 0:0 numeric, every mtime set to the option timestamp, directories
    /// 755, files 644 (755 when executable), piped through `gzip -n` at level 6. Symlinks and
    /// special files fail the step. The source tree is copied to a staging directory first and
    /// never modified. Runs in the build container; tar and gzip are installed with apt-get when
    /// missing. Value is the archive's SHA-256. Relative paths are relative to /workspace.
    /// Usage: Archive.TarGz("artifacts/api", "artifacts/api.tar.gz", o => o.WithTimestamp(Git.CommitTime));
    /// </summary>
    /// <param name="sourceDir">Directory whose contents are archived.</param>
    /// <param name="tarGzPath">Output .tar.gz path (must be outside sourceDir).</param>
    /// <param name="configure">Optional timestamp and compression level.</param>
    public CommandStep TarGz(string sourceDir, string tarGzPath, Action<ArchiveOptions>? configure = null)
    {
        var options = new ArchiveOptions();
        configure?.Invoke(options);
        return Register("Archive.TarGz", "targz", sourceDir, tarGzPath, options);
    }

    private CommandStep Register(string stepName, string kind, string sourceDir, string output, ArchiveOptions options) =>
        ShellCommandOperations.RegisterCommandStep(Registry, Logger, ExecutorFactory, stepName, "bash",
            [
                "-c", Script, "ando-archive",
                ContainerPaths.Resolve(sourceDir),
                ContainerPaths.Resolve(output),
                options.Timestamp.ToString(System.Globalization.CultureInfo.InvariantCulture),
                options.CompressionLevel.ToString(System.Globalization.CultureInfo.InvariantCulture),
                kind
            ],
            output);
}
