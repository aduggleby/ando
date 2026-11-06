---
title: Bun
description: Install Bun in Debian and Ubuntu build containers.
provider: Bun
---

## Overview

`Bun.Install` installs [Bun](https://bun.sh) into `/usr/local/bin`, so later steps (including MSBuild targets run by `Dotnet.Publish`) find `bun` on PATH. The step is skipped when the container already has the requested version, which keeps warm-container builds fast.

## Verifying a download

Pass the SHA-256 of the release zip to verify the download before it is installed:

```csharp
Bun.Install("1.2.23", sha256: "cf0ed0a920799d576ffde4e0cae66d732bf23c2530407f26f59c7831dffe1f0e");
```

`version` must then be an exact version (not `"latest"`). ANDO downloads `https://github.com/oven-sh/bun/releases/download/bun-v<version>/bun-linux-<arch>.zip`, checks the hash and fails the step with a clear message on a mismatch, installs the binary into `/usr/local/bin` and checks that `bun --version` equals the version. The hash is of the zip for the container's architecture: `bun-linux-x64.zip` on x86_64, `bun-linux-aarch64.zip` on aarch64 (the two hashes differ; the example above is the x64 one). This is the standard build, not the baseline one, so on x86_64 it needs a CPU with AVX2; on CPUs without AVX2 use the installer path without `sha256` (no hash check). With a hash the step always downloads and verifies, it does not trust a Bun that is already installed. `unzip` is installed with apt if it is missing.

It needs a Debian or Ubuntu image. If `curl` or `unzip` are missing, the step installs them with apt. The image `mcr.microsoft.com/dotnet/sdk:10.0` has curl but not unzip.

## Example

```csharp
Ando.UseImage("mcr.microsoft.com/dotnet/sdk:10.0");

// Pin an exact version, or pass "latest" (the default)
Bun.Install("1.2.23");

var app = Dotnet.Project("./src/App/App.csproj");
Dotnet.Publish(app, o => o.Output(Root / "artifacts" / "app"));
```
