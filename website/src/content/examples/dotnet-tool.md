---
title: .NET CLI Tool + NuGet
description: Build a .NET CLI tool for multiple platforms, create a NuGet package, and publish with profiles.
category: .NET
tags:
  - dotnet
  - nuget
  - cross-platform
---

## Overview

This example demonstrates building a .NET CLI tool installable via `dotnet tool install`. It publishes self-contained executables for multiple platforms and uses profiles for conditional NuGet publishing.

The workflow performs these steps:

1. Installs .NET SDK in the container
2. Restores, builds, and tests the project
3. Publishes self-contained executables for multiple platforms
4. Creates a NuGet package for the dotnet tool
5. **(with -p publish)** Publishes to NuGet.org and deploys docs
6. Copies artifacts to the host machine

## Build Script

```csharp
// Define profiles
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/Ando/Ando.csproj");
var testProject = Dotnet.Project("./tests/Ando.Tests/Ando.Tests.csproj");
var distPath = Root / "dist";

// Build workflow
Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(testProject);

// Publish for multiple platforms
var runtimes = new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" };
foreach (var runtime in runtimes)
{
    Dotnet.Publish(project, o => o
        .WithRuntime(runtime)
        .Output(distPath / runtime)
        .AsSelfContained()
        .AsSingleFile());
}

// Create NuGet package
Nuget.Pack(project);

// Push to NuGet.org (only with -p publish)
if (publish)
{
    Nuget.EnsureAuthenticated();
    Nuget.Push(project);
    Ando.Build(Directory("./website"));
}

// Copy artifacts to host
Ando.CopyArtifactsToHost("dist", "./dist");
Ando.CopyZippedArtifactsToHost("dist", "./dist/binaries.zip");
```

## Using Profiles

This script uses `DefineProfile` for conditional execution. By default it builds and tests. With the `publish` profile, it also publishes to NuGet.org.

| Command | Behavior |
|---------|----------|
| `ando` | Build, test, create NuGet package, copy artifacts |
| `ando -p publish` | All of the above + push to NuGet.org + deploy docs |

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [DefineProfile()](/providers/ando#defineprofile) | Creates a profile for conditional execution |
| [Dotnet.Publish()](/providers/dotnet#publish) | Creates self-contained executables |
| [Nuget.Pack()](/providers/nuget#pack) | Creates a NuGet package |
| [Nuget.Push()](/providers/nuget#push) | Publishes to NuGet.org |
| [Ando.Build()](/providers/ando#build) | Runs a nested build script |

## Running the Build

```bash
# Build and test only
ando

# Build, test, and push to NuGet.org
ando -p publish
```
