---
title: Bun
description: Install Bun in Debian and Ubuntu build containers.
provider: Bun
---

## Overview

`Bun.Install` installs [Bun](https://bun.sh) into `/usr/local/bin`, so later steps (including MSBuild targets run by `Dotnet.Publish`) find `bun` on PATH. The step is skipped when the container already has the requested version, which keeps warm-container builds fast.

It needs a Debian or Ubuntu image. If `curl` or `unzip` are missing, the step installs them with apt. The image `mcr.microsoft.com/dotnet/sdk:10.0` has curl but not unzip.

## Example

```csharp
Ando.UseImage("mcr.microsoft.com/dotnet/sdk:10.0");

// Pin an exact version, or pass "latest" (the default)
Bun.Install("1.2.23");

var app = Dotnet.Project("./src/App/App.csproj");
Dotnet.Publish(app, o => o.Output(Root / "artifacts" / "app"));
```
