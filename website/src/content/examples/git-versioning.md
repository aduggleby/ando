---
title: Git Versioning
description: Automatically version your releases using Git tags and semantic versioning.
category: .NET
tags:
  - git
  - versioning
  - semver
  - releases
---

## Overview

Git tags provide a reliable way to version your software. This example shows how to use ANDO's Git provider to implement semantic versioning, auto-increment versions, and create tagged releases.

## Basic Version from Git Tag

Get the current version from the latest Git tag:

```csharp
var version = Git.GetVersion();

Log.Info($"Current version: {version}");

// Use version in build
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project, o => o.WithProperty("Version", version));
```

## Auto-Increment Version

Bump the version based on commit messages or manually:

```csharp
var release = DefineProfile("release");
var major = DefineProfile("major");
var minor = DefineProfile("minor");

var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

// Get current version from tags
var currentVersion = Git.GetVersion();
Log.Info($"Current version: {currentVersion}");

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj"));

if (release)
{
    // Determine version bump type
    string newVersion;
    if (major)
    {
        newVersion = Git.BumpVersion("major");
    }
    else if (minor)
    {
        newVersion = Git.BumpVersion("minor");
    }
    else
    {
        newVersion = Git.BumpVersion("patch");
    }

    Log.Info($"New version: {newVersion}");

    // Create and push tag
    Git.Tag($"v{newVersion}");
    Git.Push();
    Git.PushTags();

    Log.Info($"Released version {newVersion}");
}
```

Usage:
```bash
# Patch release (1.0.0 -> 1.0.1)
ando -p release

# Minor release (1.0.1 -> 1.1.0)
ando -p release,minor

# Major release (1.1.0 -> 2.0.0)
ando -p release,major
```

## Version in NuGet Package

Include Git version in NuGet package:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.GetVersion();

Dotnet.Restore(project);
Dotnet.Build(project, o => o.WithProperty("Version", version));
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    // Pack with version from Git
    Dotnet.Pack(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithProperty("Version", version)
        .Output(Root / "packages"));

    // Push to NuGet
    Nuget.Push(
        Root / "packages" / $"MyLibrary.{version}.nupkg",
        Env("NUGET_API_KEY")
    );

    Log.Info($"Published MyLibrary {version} to NuGet");
}
```

## Pre-release Versions

Handle pre-release versions for CI builds:

```csharp
var release = DefineProfile("release");

var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

// Get base version from tags
var baseVersion = Git.GetVersion();
var commitCount = Git.CommitCount();
var shortSha = Git.ShortSha();

// Build version string
string version;
if (release)
{
    // Release build: use clean version
    version = baseVersion;
}
else
{
    // CI build: add pre-release suffix
    version = $"{baseVersion}-ci.{commitCount}+{shortSha}";
}

Log.Info($"Building version: {version}");

Dotnet.Restore(project);
Dotnet.Build(project, o => o.WithProperty("Version", version));
```

Example versions:
- Release: `1.2.3`
- CI build: `1.2.3-ci.47+a1b2c3d`

## GitHub Release with Changelog

Create a GitHub release with auto-generated changelog:

```csharp
var release = DefineProfile("release");

var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj"));

if (release)
{
    // Bump and tag
    var newVersion = Git.BumpVersion("patch");
    Git.Tag($"v{newVersion}");
    Git.Push();
    Git.PushTags();

    // Build release artifacts
    Dotnet.Publish(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithRuntime("win-x64")
        .Output(Root / "publish" / "win-x64"));

    Dotnet.Publish(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithRuntime("linux-x64")
        .Output(Root / "publish" / "linux-x64"));

    // Create GitHub release
    GitHub.CreateRelease($"v{newVersion}", o => o
        .WithTitle($"Release {newVersion}")
        .WithGenerateNotes()
        .WithAsset(Root / "publish" / "win-x64", "myapp-win-x64.zip")
        .WithAsset(Root / "publish" / "linux-x64", "myapp-linux-x64.zip"));

    Log.Info($"Created GitHub release v{newVersion}");
}
```

## Monorepo Versioning

Version multiple packages independently:

```csharp
var releaseCore = DefineProfile("release-core");
var releaseApi = DefineProfile("release-api");

var coreProject = Dotnet.Project("./src/Core/Core.csproj");
var apiProject = Dotnet.Project("./src/Api/Api.csproj");

// Each package has its own version tag prefix
var coreVersion = Git.GetVersion("core-v");
var apiVersion = Git.GetVersion("api-v");

Log.Info($"Core: {coreVersion}, API: {apiVersion}");

Dotnet.Restore(coreProject);
Dotnet.Restore(apiProject);
Dotnet.Build(coreProject, o => o.WithProperty("Version", coreVersion));
Dotnet.Build(apiProject, o => o.WithProperty("Version", apiVersion));

if (releaseCore)
{
    var newVersion = Git.BumpVersion("patch", "core-v");
    Git.Tag($"core-v{newVersion}");
    Git.PushTags();
    Log.Info($"Released Core {newVersion}");
}

if (releaseApi)
{
    var newVersion = Git.BumpVersion("patch", "api-v");
    Git.Tag($"api-v{newVersion}");
    Git.PushTags();
    Log.Info($"Released API {newVersion}");
}
```

Usage:
```bash
# Release core package
ando -p release-core

# Release API package
ando -p release-api
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Git.GetVersion()](/providers/git#getversion) | Get version from latest tag |
| [Git.BumpVersion()](/providers/git#bumpversion) | Increment semantic version |
| [Git.Tag()](/providers/git#tag) | Create a Git tag |
| [Git.Push()](/providers/git#push) | Push commits to remote |
| [Git.PushTags()](/providers/git#pushtags) | Push tags to remote |
| [Git.CommitCount()](/providers/git#commitcount) | Count commits since tag |
| [Git.ShortSha()](/providers/git#shortsha) | Get short commit SHA |

## Tips

- **Tag format** - Use `v` prefix consistently (e.g., `v1.2.3`)
- **Semantic versioning** - Follow semver for predictable version bumps
- **Pre-release builds** - Include commit info for traceability
- **Protect tags** - Configure branch protection to prevent tag deletion

## See Also

- [Git Provider](/providers/git) - Full API reference
- [GitHub Releases](/examples/github-releases) - Creating releases
- [NuGet Publishing](/examples/nuget-publishing) - Package versioning
