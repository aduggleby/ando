---
title: Tool installation
description: The six installer operations that put Bun, Node.js, Docker, the .NET SDK, DocFX and Playwright into the build container.
---

## Overview

ANDO builds run in a container, and base images such as `ubuntu:24.04` do not include your tools. Each installer below registers a step that installs one tool in the build container. Every installer skips the download when the tool is already present, so warm containers stay fast.

Most operations install what they need on their own (`Npm.*` installs Node.js, `Dotnet.*` installs the SDK). Call an installer explicitly to pin a version, to make a tool available to later steps that ANDO does not install for you (for example `bun` for an MSBuild target in `Dotnet.Publish`), or to be independent of the base image.

Installers have no shared provider: each one lives on the page of its tool, linked below.

| Installer                                             | Signature                                                   | What it does                                                                                                                    |
| ----------------------------------------------------- | ----------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------- |
| [`Bun.Install`](/providers/bun#install)               | `Install(string version = "latest", string? sha256 = null)` | Installs Bun into `/usr/local/bin`. With `sha256` the release zip is downloaded and verified. Needs a Debian or Ubuntu image.   |
| [`Node.Install`](/providers/node#install)             | `Install(string version = "22")`                            | Installs Node.js (with npm) globally. The version is the major version.                                                         |
| [`Docker.Install`](/providers/docker#install)         | `Install()`                                                 | Installs the Docker CLI. Required before `Docker.Build` with `--dind`; a no-op if Docker is already present.                    |
| [`Dotnet.SdkInstall`](/providers/dotnet#sdkinstall)   | `SdkInstall(string version = "9.0")`                        | Installs the .NET SDK. Use for base images without .NET.                                                                        |
| [`Docfx.Install`](/providers/docfx#install)           | `Install()`                                                 | Installs DocFX as a dotnet global tool (needs the .NET SDK).                                                                    |
| [`Playwright.Install`](/providers/playwright#install) | `Install(DirectoryRef directory)`                           | Installs Playwright browsers and system dependencies with `npx playwright install --with-deps`, in the given project directory. |

## Example

```csharp
Ando.UseImage("mcr.microsoft.com/dotnet/sdk:10.0");

Bun.Install("1.2.23");
Node.Install("22");

var e2e = Directory("./tests/E2E");
Npm.Ci(e2e);
Playwright.Install(e2e);
Playwright.Test(e2e);
```

## Notes

- Calling `Node.Install()` or `Dotnet.SdkInstall()` turns off the automatic installation for later operations, so the version you pick is the one they use.
- `Docfx.IsInstalled()` and `Docker.IsAvailable()` are not installers: they are checks evaluated when the script loads, on the host. See the [DocFX](/providers/docfx) and [Docker](/providers/docker) pages.
