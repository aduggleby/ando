---
title: Files
description: Copy files and directories inside the build container.
provider: Files
---

## Overview

`Files.Copy` is a step that copies a file or directory inside the build container. Relative paths are relative to `/workspace`. Missing parent directories of the destination are created, and `cp -a` preserves modes, times and symlinks.

```csharp
// File to a file path (overwrites an existing file)
Files.Copy("config/prod.json", "artifacts/web/config.json");

// File into a directory (the destination ends with "/")
Files.Copy("LICENSE", "artifacts/web/");

// Directory contents into a directory; skipped when "public" does not exist
Files.Copy("public", "artifacts/web", optional: true);
```

## Semantics

| Source    | Destination                                                                                                                                                                                                                                                                          |
| --------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| File      | The target file path. An existing file is overwritten. If the destination ends with `/`, the file is copied into that directory under its own name.                                                                                                                                  |
| Directory | The target directory. The contents of the source are copied in, like `cp -a src/. dst/`. The destination is created if missing; existing contents merge and same-named files are overwritten. The source directory name is never added: copying `a` to `b` gives `b/x`, not `b/a/x`. |

A missing source fails the step ("source not found"). With `optional: true` the step succeeds and logs that it was skipped.

## Related

- Copy between the host and the container with [Ando.CopyToContainer](/providers/ando#copytocontainer) and [Ando.CopyArtifactsToHost](/providers/ando#copyartifactstohost).
- Pack a directory into a deterministic archive with [Archive.Zip](/providers/archive#zip) or [Archive.TarGz](/providers/archive#targz).
