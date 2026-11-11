---
title: Archive
description: Create byte-identical zip and tar.gz archives of a directory.
provider: Archive
---

## Overview

`Archive.Zip` and `Archive.TarGz` create archives that are byte-identical for the same source tree. They are steps that run in the build container and return a `CommandStep` whose `Value` is the SHA-256 (hex) of the archive.

```csharp
// Archive the published site, with timestamps taken from the commit
Archive.Zip("artifacts/web", "artifacts/web.zip", o => o.WithTimestamp(Git.CommitTime));

// A tar.gz with the strongest compression
Archive.TarGz("artifacts/app", "artifacts/app.tar.gz", o => o.WithCompressionLevel(9));

// Record the hash of the archive in the run record
var sha = Archive.Zip("publish", "out/func.zip");
Ando.Report("func.zip sha256", () => sha.Value);
```

## Options

| Option                      | Description                                                                                                                                                                                                       |
| --------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WithTimestamp(long)`       | Unix seconds used as the modification time of every entry. Default `315532800` (1980-01-01 UTC). `Zip` rejects earlier values and stores 2-second resolution. Use `Git.CommitTime` to tie an archive to a commit. |
| `WithCompressionLevel(int)` | Compression level `1` to `9`. Default `6`.                                                                                                                                                                        |

## What is guaranteed

For the same source tree, options and image, the output is byte-identical:

- Entries are sorted by path (`LC_ALL=C`), directory entries are included, and there is no `./` prefix.
- Every modification time is the timestamp.
- Directories get mode 755. Files get 755 if any execute bit is set, else 644.
- Zip uses `zip -X` (no extra fields, no uid/gid). Tar uses `--format=gnu --sort=name` with owner and group `0:0` numeric, then `gzip -n`.

## Rules and caveats

- File names with dashes, spaces, quotes and backslashes are safe. Names containing a newline are rejected.
- Symlinks and special files fail with a clear message that lists up to 5 of them.
- `Archive.Zip` needs a path ending in `.zip`.
- The output may not equal the source or sit inside it. This is checked on resolved paths.
- Hardlinks are dereferenced, so the bytes equal those of a copied tree.
- The source tree is never modified: it is staged in a temporary copy.
- `zip`, `tar` and `gzip` are installed with apt when missing, so a Debian or Ubuntu image is needed.
- Identity holds for the same zip and gzip builds, that is, the same image. Different tool builds may compress differently.
- To get the archive to the host, use `Ando.CopyArtifactsToHost`. To copy files into the archive source first, use [Files.Copy](/providers/files#copy).
