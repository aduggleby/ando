---
title: Git Versioning
description: Version your releases with Git tags and semantic versioning.
category: .NET
tags:
  - git
  - versioning
  - semver
  - releases
---

## Overview

Git tags provide a reliable way to version your software. This example shows how to read the latest tag, name pre-release builds, create and push tags, and attach artifacts to a GitHub release with ANDO's Git, Nuget and GitHub providers.

`Git.LastTag()` is evaluated when the script loads, on the host, so its result can be used in plain C# (`if`, string interpolation). To bump the version in your project files and write the changelog, use the `ando bump` command instead of computing it in the build script.

## Basic Version from Git Tag

Read the latest `v*` tag and use it for a NuGet package version:

```csharp
// "v1.2.3" -> "1.2.3"; null when no tag exists yet
var version = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

Log.Info($"Current version: {version}");

var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);
```

`Dotnet.Build` has no option to set MSBuild properties, so the `Version` of an assembly comes from the project file. Packages can take the version from the tag (see below).

## Tag and Push a Release

Create the tag from a version you computed in C# and push it:

```csharp
var release = DefineProfile("release");

var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj"));

if (release)
{
    // Next patch version from the latest tag (plain C#, evaluated when the script loads)
    var current = Version.Parse(Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0");
    var next = new Version(current.Major, current.Minor, current.Build + 1);

    Git.Tag($"v{next}");
    Git.Push();
    Git.PushTags();

    Log.Info($"Released version {next}");
}
```

Usage:

```bash
ando -p release
```

## Version in NuGet Package

`Nuget.Pack` accepts a version and a pre-release suffix:

```csharp
var publish = DefineProfile("publish");

var project = Dotnet.Project("./src/MyLibrary/MyLibrary.csproj");
var version = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyLibrary.Tests/MyLibrary.Tests.csproj"));

if (publish)
{
    Nuget.EnsureAuthenticated();
    Nuget.Pack(project, o => o
        .WithVersion(version)
        .Output(Root / "packages"));
    Nuget.Push(project);

    Log.Info($"Published MyLibrary {version} to NuGet");
}
```

## Pre-release Versions

Name CI builds with a suffix and the short commit:

```csharp
var release = DefineProfile("release");

var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
var baseVersion = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

Dotnet.Restore(project);
Dotnet.Build(project);

// Release build: clean version. CI build: pre-release suffix with the short commit.
var version = release ? baseVersion : $"{baseVersion}-ci.{Git.ShortCommit}";
Log.Info($"Building version: {version}");

Nuget.Pack(project, o => o.WithVersion(version).Output(Root / "packages"));
```

Example versions:

- Release: `1.2.3`
- CI build: `1.2.3-ci.a1b2c3d`

## GitHub Release with Changelog

Create a GitHub release with generated notes and uploaded files (`path#name` renames an uploaded file):

```csharp
var release = DefineProfile("release");

var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj"));

if (release)
{
    var tag = "v1.2.3";

    Dotnet.Publish(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithRuntime("win-x64")
        .Output(Root / "publish" / "win-x64"));

    Dotnet.Publish(project, o => o
        .WithConfiguration(Configuration.Release)
        .WithRuntime("linux-x64")
        .Output(Root / "publish" / "linux-x64"));

    Git.Tag(tag);
    Git.Push();
    Git.PushTags();

    GitHub.CreateRelease(o => o
        .WithTag(tag)
        .WithTitle($"Release {tag}")
        .WithGeneratedNotes()
        .WithFiles(
            "publish/win-x64/MyApp.exe#myapp-win-x64.exe",
            "publish/linux-x64/MyApp#myapp-linux-x64"));

    Log.Info($"Created GitHub release {tag}");
}
```

## Monorepo Versioning

Version several packages independently with their own tag prefix, and build only what changed:

```csharp
var coreProject = Dotnet.Project("./src/Core/Core.csproj");
var apiProject = Dotnet.Project("./src/Api/Api.csproj");

var coreTag = Git.LastTag("core-v*");
var apiTag = Git.LastTag("api-v*");

Dotnet.Restore(coreProject);
Dotnet.Restore(apiProject);

// Build a package when it changed since its last tag (or has no tag yet)
if (coreTag == null || Git.ChangedSince(coreTag, "src/Core"))
{
    Dotnet.Build(coreProject);
    Log.Info($"Core changed since {coreTag ?? "the start"}");
}

if (apiTag == null || Git.ChangedSince(apiTag, "src/Api"))
{
    Dotnet.Build(apiProject);
    Log.Info($"API changed since {apiTag ?? "the start"}");
}
```

## Step-based Alternatives

`Git.LastTag`, `Git.Commit`, `Git.ShortCommit` and `Git.ChangedSince` are evaluated when the script loads. When the value must be read in order with other steps, or must show up in `--dry-run`, use the step variants and read `Value` at execution time:

```csharp
var tag = Git.LatestTag("v*");
var sha = Git.RevParse(shortSha: true);
var changed = Git.ChangedFiles("HEAD~1", "src");

Ando.When(() => changed.Value != "", () => Log.Info($"Changed since {tag.Value} at {sha.Value}"));
```

## Key Operations

| Operation                                                                                                                                 | Purpose                                                             |
| ----------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------- |
| [Git.LastTag()](/providers/git#lasttag)                                                                                                   | Latest tag matching a glob (evaluated when the script loads)        |
| [Git.ShortCommit](/providers/git#shortcommit)                                                                                             | Short commit SHA (evaluated when the script loads)                  |
| [Git.ChangedSince()](/providers/git#changedsince)                                                                                         | True if paths changed since a ref (evaluated when the script loads) |
| [Git.LatestTag()](/providers/git#latesttag), [Git.RevParse()](/providers/git#revparse), [Git.ChangedFiles()](/providers/git#changedfiles) | Step-based counterparts                                             |
| [Git.Tag()](/providers/git#tag)                                                                                                           | Create a Git tag                                                    |
| [Git.Push()](/providers/git#push)                                                                                                         | Push the current branch                                             |
| [Git.PushTags()](/providers/git#pushtags)                                                                                                 | Push tags to remote                                                 |
| [Nuget.Pack()](/providers/nuget#pack)                                                                                                     | Pack with `WithVersion`                                             |
| [GitHub.CreateRelease()](/providers/github#createrelease)                                                                                 | Create a release with notes and files                               |

## Tips

- **Tag format** - Use `v` prefix consistently (e.g., `v1.2.3`)
- **Semantic versioning** - Follow semver for predictable version bumps
- **Bumping project files** - `ando bump` updates the version in your project files and writes the changelog
- **Pre-release builds** - Include commit info for traceability
- **Protect tags** - Configure branch protection to prevent tag deletion

## See Also

- [Git Provider](/providers/git) - Full API reference
- [GitHub Releases](/examples/github-releases) - Creating releases
- [NuGet Publishing](/examples/nuget-publishing) - Package versioning
