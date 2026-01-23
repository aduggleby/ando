---
title: NuGet
description: Create and publish NuGet packages to nuget.org or private feeds.
provider: Nuget
---

## Authentication

NuGet.org requires an API key for publishing packages. You can generate one at [nuget.org/account/apikeys](https://www.nuget.org/account/apikeys).

For CI/CD, set the `NUGET_API_KEY` environment variable and access it via `Vars["NUGET_API_KEY"]`.

## Example

Build a library and publish to NuGet.org.

```csharp
// Define project reference
var App = Dotnet.Project("./src/MyLib/MyLib.csproj");

// Build the project
Dotnet.Build(App);

// Create NuGet package (outputs to bin/Release)
Nuget.Pack(App);

// Authenticate and push to NuGet.org
// Defaults: nuget.org, skips if version already exists
Nuget.EnsureAuthenticated();
Nuget.Push(App);
```

## Options Reference

### Nuget.Pack Options

| Option | Description |
|--------|-------------|
| `Output(path)` | Output directory for the generated .nupkg file. Defaults to `bin/{Configuration}`. |
| `WithConfiguration(Configuration)` | Build configuration. Use `Configuration.Release` for publishing to nuget.org. |
| `WithVersion(string)` | Explicit package version, overriding the version in the .csproj. Useful for CI-generated version numbers. |
| `WithVersionSuffix(string)` | Version suffix for pre-release packages (e.g., "beta", "rc1"). Combined with base version: "1.0.0-beta". |
| `WithSymbols(bool)` | Generate a symbol package (.snupkg) for source-level debugging. Publish to nuget.org symbol server for public packages. |
| `WithSource(bool)` | Include source files in the symbol package. Enables stepping into your library code during debugging. |
| `SkipRestore()` | Skip restore before packing. Use when dependencies are already restored. |
| `SkipBuild()` | Skip build before packing. Use when the project is already built. |

### Nuget.Push Options

| Option | Description |
|--------|-------------|
| `ToSource(string)` | NuGet feed URL to push to. Use for private feeds or self-hosted NuGet servers. |
| `ToNuGetOrg()` | Push to nuget.org (the public NuGet gallery). This is the default destination. |
| `WithApiKey(string)` | API key for authentication. If not specified, uses `NUGET_API_KEY` environment variable. |
| `SkipDuplicates(bool)` | Skip push if the version already exists on the feed. Prevents errors when re-running builds. Defaults to `true`. |
| `WithoutSymbols(bool)` | Don't push symbol packages (.snupkg). Use if your feed doesn't support symbols or you want to push them separately. |
