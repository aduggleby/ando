---
title: GitHub Release Management
description: Create GitHub releases with auto-generated notes, version tags, and file uploads.
difficulty: beginner
tags:
  - github
  - releases
  - versioning
  - ci-cd
---

## Overview

GitHub releases provide a way to package and distribute software versions. This recipe shows how to automate release creation with version tags, auto-generated release notes, and binary uploads.

## Basic Release

The simplest release workflow tags the current commit and creates a release with auto-generated notes:

```csharp
var version = "1.0.0";

// Create and push the tag
Git.Tag($"v{version}");
Git.Push();
Git.PushTags();

// Create the release with auto-generated notes
GitHub.CreateRelease(o => o
    .WithTag($"v{version}")
    .WithGeneratedNotes());
```

GitHub's auto-generated notes include:
- All commits since the last release
- PR titles for merged pull requests
- List of contributors

## Release with Custom Notes

For more control over release notes, use `WithNotes()`:

```csharp
var version = "1.2.0";
var releaseNotes = @"
## What's New

- Added user authentication system
- Improved performance by 40%
- Fixed memory leak in data processor

## Breaking Changes

- Removed deprecated `ProcessData()` method
- Changed default timeout from 30s to 60s

## Contributors

Thanks to @alice and @bob for their contributions!
";

Git.Tag($"v{version}");
Git.Push();
Git.PushTags();

GitHub.CreateRelease(o => o
    .WithTag($"v{version}")
    .WithTitle($"v{version} - Authentication & Performance")
    .WithNotes(releaseNotes));
```

## Release with Binary Uploads

Upload compiled binaries, installers, or other files as release assets:

```csharp
var version = "1.0.0";
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

// Build for multiple platforms
var runtimes = new[] { "win-x64", "linux-x64", "osx-x64", "osx-arm64" };

foreach (var runtime in runtimes)
{
    Dotnet.Publish(project, o => o
        .WithRuntime(runtime)
        .Output(Root / "dist" / runtime)
        .WithConfiguration(Configuration.Release)
        .AsSelfContained()
        .AsSingleFile());
}

// Copy artifacts to host for upload
Ando.CopyArtifactsToHost("dist", "./dist");

// Create release with file uploads
Git.Tag($"v{version}");
Git.Push();
Git.PushTags();

GitHub.CreateRelease(o => o
    .WithTag($"v{version}")
    .WithGeneratedNotes()
    .WithFiles(
        "dist/win-x64/MyApp.exe",
        "dist/linux-x64/MyApp",
        "dist/osx-x64/MyApp",
        "dist/osx-arm64/MyApp"));
```

### Renaming Files in Releases

Use the `path#name` syntax to give uploaded files descriptive names:

```csharp
GitHub.CreateRelease(o => o
    .WithTag($"v{version}")
    .WithGeneratedNotes()
    .WithFiles(
        "dist/win-x64/MyApp.exe#MyApp-windows-x64.exe",
        "dist/linux-x64/MyApp#MyApp-linux-x64",
        "dist/osx-x64/MyApp#MyApp-macos-x64",
        "dist/osx-arm64/MyApp#MyApp-macos-arm64"));
```

Users will see clean names like `MyApp-windows-x64.exe` instead of just `MyApp.exe`.

## Pre-releases and Drafts

### Pre-release Versions

Mark releases as pre-releases for beta or release candidate versions:

```csharp
var version = "2.0.0-beta.1";

Git.Tag($"v{version}");
Git.Push();
Git.PushTags();

GitHub.CreateRelease(o => o
    .WithTag($"v{version}")
    .WithTitle($"v{version} (Beta)")
    .WithGeneratedNotes()
    .AsPrerelease());
```

Pre-releases:
- Display a "Pre-release" label on GitHub
- Are excluded from the "latest release" API
- Allow users to opt-in to beta testing

### Draft Releases

Create draft releases for review before publishing:

```csharp
GitHub.CreateRelease(o => o
    .WithTag($"v{version}")
    .WithGeneratedNotes()
    .AsDraft());
```

Draft releases:
- Are invisible to the public
- Can be edited in the GitHub UI
- Must be manually published

## Conditional Releases with Profiles

Use profiles to separate build and release steps:

```csharp
var release = DefineProfile("release");

var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
var version = project.Version; // Read from .csproj

// Always build and test
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj"));

// Only release when profile is active
if (release)
{
    // Publish binaries
    Dotnet.Publish(project, o => o
        .Output(Root / "dist")
        .WithConfiguration(Configuration.Release)
        .AsSelfContained());

    Ando.CopyArtifactsToHost("dist", "./dist");

    // Tag and release
    Git.Tag($"v{version}", o => o.WithSkipIfExists());
    Git.Push();
    Git.PushTags();

    GitHub.CreateRelease(o => o
        .WithTag($"v{version}")
        .WithGeneratedNotes()
        .WithFiles("dist/MyApp"));
}
```

Usage:
```bash
# Build and test only
ando

# Build, test, and create release
ando -p release
```

## Version from Project File

Read the version from your `.csproj` file for single-source-of-truth versioning:

```csharp
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
var version = project.Version; // Reads <Version> from .csproj

Log.Info($"Building version {version}");

// Use version throughout the build
Dotnet.Build(project);

GitHub.CreateRelease(o => o
    .WithTag($"v{version}")
    .WithTitle($"MyApp v{version}")
    .WithGeneratedNotes());
```

## Full Example: .NET Tool Release

Complete example for releasing a .NET CLI tool:

```csharp
// build.csando
var release = DefineProfile("release");

var project = Dotnet.Project("./src/MyTool/MyTool.csproj");
var tests = Dotnet.Project("./tests/MyTool.Tests/MyTool.Tests.csproj");
var version = project.Version;

// Build and test
Dotnet.SdkInstall();
Dotnet.Build(project);
Dotnet.Test(tests);

if (release)
{
    // Publish for all platforms
    var runtimes = new[] { "win-x64", "linux-x64", "linux-arm64", "osx-x64", "osx-arm64" };

    foreach (var runtime in runtimes)
    {
        Log.Info($"Publishing for {runtime}...");
        Dotnet.Publish(project, o => o
            .WithRuntime(runtime)
            .Output(Root / "dist" / runtime)
            .WithConfiguration(Configuration.Release)
            .AsSelfContained()
            .AsSingleFile());
    }

    // Create NuGet package
    Nuget.Pack(project);

    // Copy to host
    Ando.CopyArtifactsToHost("dist", "./dist");

    // Create GitHub release
    Git.Tag($"v{version}", o => o.WithSkipIfExists());
    Git.Push();
    Git.PushTags();

    GitHub.CreateRelease(o => o
        .WithTag($"v{version}")
        .WithTitle($"v{version}")
        .WithGeneratedNotes()
        .WithFiles(
            $"dist/win-x64/MyTool.exe#mytool-win-x64.exe",
            $"dist/linux-x64/MyTool#mytool-linux-x64",
            $"dist/linux-arm64/MyTool#mytool-linux-arm64",
            $"dist/osx-x64/MyTool#mytool-macos-x64",
            $"dist/osx-arm64/MyTool#mytool-macos-arm64"));

    // Push to NuGet.org
    Nuget.EnsureAuthenticated();
    Nuget.Push(project);
}
```

## Options Reference

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Tag name for the release (e.g., "v1.0.0") |
| `WithTitle(string)` | Release title (defaults to tag name) |
| `WithNotes(string)` | Markdown release notes |
| `WithGeneratedNotes()` | Auto-generate notes from commits |
| `AsDraft()` | Create as draft (invisible until published) |
| `AsPrerelease()` | Mark as pre-release |
| `WithoutPrefix()` | Don't add 'v' prefix to tag |
| `WithFiles(params string[])` | Files to upload as assets |

## See Also

- [GitHub Provider](/providers/github) - GitHub operations reference
- [Git Provider](/providers/git) - Git operations reference
- [Dotnet Provider](/providers/dotnet) - .NET build operations
