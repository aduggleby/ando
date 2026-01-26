---
title: NuGet Packages with GitHub Packages
description: Build and publish NuGet packages to GitHub Packages registry.
difficulty: intermediate
tags:
  - nuget
  - github
  - packages
  - dotnet
---

## Overview

GitHub Packages provides a NuGet registry alongside your repository. This recipe shows how to build NuGet packages and publish them to GitHub Packages, making it easy to share internal libraries within your organization.

## Prerequisites

### Authentication

Publishing to GitHub Packages requires a personal access token (PAT) or GitHub Actions token with the `write:packages` scope.

| Environment Variable | Description |
|---------------------|-------------|
| `GITHUB_TOKEN` | Token for GitHub Packages authentication |
| `NUGET_API_KEY` | Alternative: can use same token |

### GitHub Packages Feed URL

The feed URL format is:
```
https://nuget.pkg.github.com/{OWNER}/index.json
```

Replace `{OWNER}` with your GitHub username or organization name.

## Basic Package Publishing

Build and publish a package to GitHub Packages:

```csharp
var project = Dotnet.Project("./src/MyLib/MyLib.csproj");

// Build the project
Dotnet.Build(project);

// Create NuGet package
Nuget.Pack(project, o => o
    .WithConfiguration(Configuration.Release));

// Push to GitHub Packages
var owner = "my-username"; // or organization name
Nuget.Push(project, o => o
    .ToSource($"https://nuget.pkg.github.com/{owner}/index.json")
    .WithApiKey(Env("GITHUB_TOKEN")));
```

## Package with Symbols

Include symbols for debugging:

```csharp
Nuget.Pack(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithSymbols()
    .WithSource());

Nuget.Push(project, o => o
    .ToSource($"https://nuget.pkg.github.com/{owner}/index.json")
    .WithApiKey(Env("GITHUB_TOKEN")));
```

Note: GitHub Packages doesn't have a separate symbol server, but symbols are included in the package.

## Version Management

### Version from Project File

Set the version in your `.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Version>1.2.3</Version>
    <PackageId>MyCompany.MyLib</PackageId>
    <Authors>My Company</Authors>
    <Description>A useful library</Description>
  </PropertyGroup>
</Project>
```

Then use it in your build:

```csharp
var project = Dotnet.Project("./src/MyLib/MyLib.csproj");
var version = project.Version;

Log.Info($"Building {project.Name} v{version}");

Dotnet.Build(project);
Nuget.Pack(project);
Nuget.Push(project, o => o
    .ToSource($"https://nuget.pkg.github.com/{owner}/index.json")
    .WithApiKey(Env("GITHUB_TOKEN")));
```

### Pre-release Versions

Create pre-release packages with version suffixes:

```csharp
// For CI builds, append build number
var buildNumber = Env("BUILD_NUMBER", required: false) ?? "local";

Nuget.Pack(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithVersionSuffix($"preview.{buildNumber}"));

// Creates: MyLib.1.2.3-preview.42.nupkg
```

### Override Version Entirely

```csharp
var version = "2.0.0-beta.1";

Nuget.Pack(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithVersion(version));
```

## Conditional Publishing with Profiles

Separate build and publish:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLib/MyLib.csproj");
var tests = Dotnet.Project("./tests/MyLib.Tests/MyLib.Tests.csproj");
var owner = "my-organization";

// Always build and test
Dotnet.Build(project);
Dotnet.Test(tests);

// Always create package (for verification)
Nuget.Pack(project, o => o
    .WithConfiguration(Configuration.Release)
    .Output(Root / "packages"));

// Only push with -p publish
if (publish)
{
    Nuget.Push(Root / "packages" / "*.nupkg", o => o
        .ToSource($"https://nuget.pkg.github.com/{owner}/index.json")
        .WithApiKey(Env("GITHUB_TOKEN"))
        .SkipDuplicates());

    Log.Info($"Published to GitHub Packages: {project.Name} v{project.Version}");
}
```

Usage:
```bash
# Build, test, and create package
ando

# Build, test, create package, and push
ando -p publish
```

## Multi-Project Publishing

Publish multiple packages from a solution:

```csharp
var publish = DefineProfile("publish");
var owner = "my-organization";

var projects = new[]
{
    Dotnet.Project("./src/MyLib.Core/MyLib.Core.csproj"),
    Dotnet.Project("./src/MyLib.Extensions/MyLib.Extensions.csproj"),
    Dotnet.Project("./src/MyLib.Testing/MyLib.Testing.csproj"),
};

// Build and test all
foreach (var project in projects)
{
    Dotnet.Build(project, o => o.Configuration = Configuration.Release);
}

Dotnet.Test(Dotnet.Project("./tests/MyLib.Tests/MyLib.Tests.csproj"));

// Pack all
foreach (var project in projects)
{
    Nuget.Pack(project, o => o
        .WithConfiguration(Configuration.Release)
        .Output(Root / "packages"));
}

// Push all
if (publish)
{
    Nuget.Push(Root / "packages" / "*.nupkg", o => o
        .ToSource($"https://nuget.pkg.github.com/{owner}/index.json")
        .WithApiKey(Env("GITHUB_TOKEN"))
        .SkipDuplicates());
}
```

## GitHub Actions Integration

### Workflow File

```yaml
# .github/workflows/nuget.yml
name: NuGet Package

on:
  push:
    branches: [main]
    tags: ['v*']
  pull_request:
    branches: [main]

jobs:
  build:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Install ANDO
        run: dotnet tool install -g ando

      - name: Build and test
        run: ando

      - name: Publish package
        if: github.event_name == 'push' && startsWith(github.ref, 'refs/tags/')
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: ando -p publish
```

### Package Consumption

To consume packages from GitHub Packages, add a `nuget.config` to your project:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
    <add key="github" value="https://nuget.pkg.github.com/OWNER/index.json" />
  </packageSources>
  <packageSourceCredentials>
    <github>
      <add key="Username" value="USERNAME" />
      <add key="ClearTextPassword" value="TOKEN" />
    </github>
  </packageSourceCredentials>
</configuration>
```

Or authenticate via environment:
```bash
dotnet nuget add source https://nuget.pkg.github.com/OWNER/index.json \
  --name github \
  --username USERNAME \
  --password $GITHUB_TOKEN
```

## Dual Publishing: GitHub Packages + NuGet.org

Publish to both registries:

```csharp
var pushGitHub = DefineProfile("push-github");
var pushNuGet = DefineProfile("push-nuget");

var project = Dotnet.Project("./src/MyLib/MyLib.csproj");
var owner = "my-organization";

Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyLib.Tests/MyLib.Tests.csproj"));

Nuget.Pack(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithSymbols()
    .Output(Root / "packages"));

// Push to GitHub Packages
if (pushGitHub)
{
    Nuget.Push(Root / "packages" / "*.nupkg", o => o
        .ToSource($"https://nuget.pkg.github.com/{owner}/index.json")
        .WithApiKey(Env("GITHUB_TOKEN"))
        .SkipDuplicates());

    Log.Info("Published to GitHub Packages");
}

// Push to NuGet.org
if (pushNuGet)
{
    Nuget.EnsureAuthenticated();
    Nuget.Push(Root / "packages" / "*.nupkg", o => o
        .ToNuGetOrg()
        .SkipDuplicates());

    Log.Info("Published to NuGet.org");
}
```

Usage:
```bash
# Push to GitHub Packages only
ando -p publish-github

# Push to NuGet.org only
ando -p publish-nuget

# Push to both
ando -p publish-github,push-nuget
```

## Full Example: Internal Library

Complete build script for an internal library:

```csharp
// build.csando
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyCompany.Utilities/MyCompany.Utilities.csproj");
var tests = Dotnet.Project("./tests/MyCompany.Utilities.Tests/MyCompany.Utilities.Tests.csproj");
var owner = "my-company";
var version = project.Version;

// Install SDK if needed
Dotnet.SdkInstall();

// Build
Log.Info($"Building {project.Name} v{version}");
Dotnet.Build(project, o => o.Configuration = Configuration.Release);

// Test
Dotnet.Test(tests);

// Pack
Nuget.Pack(project, o => o
    .WithConfiguration(Configuration.Release)
    .WithSymbols()
    .WithSource()
    .Output(Root / "packages"));

// Publish
if (publish)
{
    var packagePath = Root / "packages" / $"{project.Name}.{version}.nupkg";

    Nuget.Push(packagePath, o => o
        .ToSource($"https://nuget.pkg.github.com/{owner}/index.json")
        .WithApiKey(Env("GITHUB_TOKEN"))
        .SkipDuplicates());

    Log.Info($"Published {project.Name} v{version} to GitHub Packages");
    Log.Info($"Install with: dotnet add package {project.Name} --version {version}");
}

// Copy packages to host for inspection
Ando.CopyArtifactsToHost("packages", "./packages");
```

## Handling Duplicate Versions

By default, `SkipDuplicates()` is enabled, which skips publishing if the version already exists. This is useful for idempotent builds.

To fail on duplicates (enforce unique versions):

```csharp
Nuget.Push(project, o => o
    .ToSource($"https://nuget.pkg.github.com/{owner}/index.json")
    .WithApiKey(Env("GITHUB_TOKEN"))
    .SkipDuplicates(false));  // Will fail if version exists
```

## Options Reference

### Nuget.Pack Options

| Option | Description |
|--------|-------------|
| `Output(path)` | Output directory for .nupkg |
| `WithConfiguration(Configuration)` | Build configuration |
| `WithVersion(string)` | Override package version |
| `WithVersionSuffix(string)` | Pre-release suffix (e.g., "beta.1") |
| `WithSymbols(bool)` | Include .snupkg for debugging |
| `WithSource(bool)` | Include source in symbols |
| `SkipRestore()` | Skip restore before packing |
| `SkipBuild()` | Skip build before packing |

### Nuget.Push Options

| Option | Description |
|--------|-------------|
| `ToSource(string)` | NuGet feed URL |
| `ToNuGetOrg()` | Push to nuget.org |
| `WithApiKey(string)` | API key for authentication |
| `SkipDuplicates(bool)` | Skip if version exists (default: true) |
| `WithoutSymbols(bool)` | Don't push .snupkg |

## See Also

- [NuGet Provider](/providers/nuget) - NuGet operations reference
- [Dotnet Provider](/providers/dotnet) - .NET build operations
- [GitHub Releases Recipe](/recipes/github-releases) - Creating GitHub releases
