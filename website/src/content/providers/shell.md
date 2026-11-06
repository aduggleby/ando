---
title: Shell
description: Run any command or bash script in the build container.
provider: Shell
---

## Overview

`Shell.Run` and `Shell.Bash` register a step that runs a command in the build container, in order with the other steps. Use them for tools that have no ANDO operation. The step fails the build on a non-zero exit code unless you call `AllowFailure()`.

Both return a `CommandStep`. Its output can be read at execution time by later steps (see the Ando provider page, "Step model").

Hook scripts have a different `Shell` global (`Shell.RunAsync`). The operations on this page are for `build.csando`.

## Example

```csharp
// Zip a folder and use a helper script as a gate
Shell.Run("zip", "-qr", "../web.zip", ".").InDirectory("artifacts/web");
Shell.Bash("source scripts/lib/deploy-gates.sh && require_spa_build artifacts/web");

// Read the output later, at execution time
var version = Shell.Run("cat", "VERSION").Quiet();
Ando.Report("version", () => version.Value);
```

## Notes

- `Shell.Run` passes arguments as a list, with no shell parsing. Use `Shell.Bash` for pipes, redirection and sourcing scripts.
- `Shell.Bash` runs `bash -euo pipefail -c <script>`, so a failing command in a pipeline fails the step.
- `InDirectory(path)`: relative paths are relative to `/workspace`.
- `WithTimeout(TimeSpan)`: the default is 5 minutes, or the value set with `Ando.CommandTimeout`.
- `WithEnv(name, value)` sets an environment variable for this command only.
- `AllowFailure()` lets the build continue on a non-zero exit. Check `ExitCode` in a later `Ando.When` or `Ando.Require`.
- `Quiet()` captures the output without printing it.
- `Value` is the trimmed stdout. Reading `Value`, `ExitCode` or `Json(path)` before the step ran throws.
- The image `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04) has curl, tar, openssl, git and bash, but not zip or unzip. Install them in a step if you need them.
