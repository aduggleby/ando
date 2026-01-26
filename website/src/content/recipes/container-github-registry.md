---
title: Container Building and Publishing to GitHub
description: Build Docker images and push to GitHub Container Registry (ghcr.io).
difficulty: beginner
tags:
  - docker
  - github
  - containers
  - ghcr
---

## Overview

GitHub Container Registry (ghcr.io) provides free container hosting for public repositories and affordable storage for private ones. This recipe shows how to build Docker images and push them to ghcr.io using ANDO.

## Prerequisites

### Authentication

GitHub operations require authentication. ANDO checks these sources in order:

| Method | Description |
|--------|-------------|
| `GITHUB_TOKEN` | Environment variable (preferred for CI/CD) |
| `gh auth login` | Uses token from gh CLI if logged in locally |

For CI/CD, use the built-in `GITHUB_TOKEN` in GitHub Actions. For local development, run `gh auth login` once.

### Token Permissions

Your token needs the `write:packages` scope to push images. Create a token at [github.com/settings/tokens](https://github.com/settings/tokens).

## Basic Container Build and Push

Build a Docker image and push it to ghcr.io:

```csharp
var version = "1.0.0";

// Build the Docker image
Docker.Build("Dockerfile", o => o
    .WithTag($"myapp:{version}"));

// Push to GitHub Container Registry
GitHub.PushImage("myapp", o => o
    .WithTag(version));
```

The image will be available at `ghcr.io/{owner}/myapp:1.0.0`.

## Multi-tag Images

Push the same image with multiple tags (version + latest):

```csharp
var version = "1.2.3";

// Build with multiple tags
Docker.Build("Dockerfile", o => o
    .WithTag($"myapp:{version}")
    .WithTag("myapp:latest"));

// Push both tags
GitHub.PushImage("myapp", o => o.WithTag(version));
GitHub.PushImage("myapp", o => o.WithTag("latest"));
```

## Full .NET Application Workflow

Build a .NET application, containerize it, and push to ghcr.io:

```csharp
var version = "1.0.0";
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

// Build the .NET application
Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project, o => o.Configuration = Configuration.Release);
Dotnet.Test(Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj"));

// Publish for Linux container
Dotnet.Publish(project, o => o
    .Output(Root / "publish")
    .WithConfiguration(Configuration.Release)
    .WithRuntime("linux-x64"));

// Build Docker image
Docker.Build("Dockerfile", o => o
    .WithTag($"myapp:{version}")
    .WithTag("myapp:latest"));

// Push to ghcr.io
GitHub.PushImage("myapp", o => o.WithTag(version));
GitHub.PushImage("myapp", o => o.WithTag("latest"));
```

### Dockerfile Example

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY publish/ .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

## Specifying the Owner

By default, `GitHub.PushImage` detects the owner from your git remote. To specify explicitly:

```csharp
// Push to an organization's registry
GitHub.PushImage("myapp", o => o
    .WithTag(version)
    .WithOwner("my-organization"));

// Result: ghcr.io/my-organization/myapp:1.0.0
```

## Build Arguments and Platforms

### Passing Build Arguments

Inject version numbers or build metadata into the image:

```csharp
var version = "1.0.0";
var buildNumber = Env("BUILD_NUMBER", required: false) ?? "local";

Docker.Build("Dockerfile", o => o
    .WithTag($"myapp:{version}")
    .WithBuildArg("VERSION", version)
    .WithBuildArg("BUILD_NUMBER", buildNumber));
```

Access in Dockerfile:
```dockerfile
ARG VERSION
ARG BUILD_NUMBER
ENV APP_VERSION=${VERSION}
ENV APP_BUILD=${BUILD_NUMBER}
```

### Cross-Platform Builds

Build for a specific platform (useful for ARM servers):

```csharp
Docker.Build("Dockerfile", o => o
    .WithTag($"myapp:{version}")
    .WithPlatform("linux/amd64"));

// Or for ARM64 (e.g., AWS Graviton, Apple Silicon)
Docker.Build("Dockerfile", o => o
    .WithTag($"myapp:{version}")
    .WithPlatform("linux/arm64"));
```

## Conditional Publishing with Profiles

Separate build and publish steps:

```csharp
var publish = DefineProfile("publish");
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
var version = project.Version;

// Always build and test
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj"));

// Always build the Docker image (for verification)
Dotnet.Publish(project, o => o
    .Output(Root / "publish")
    .WithConfiguration(Configuration.Release));

Docker.Build("Dockerfile", o => o
    .WithTag($"myapp:{version}")
    .WithTag("myapp:latest"));

// Only push with -p publish
if (publish)
{
    GitHub.PushImage("myapp", o => o.WithTag(version));
    GitHub.PushImage("myapp", o => o.WithTag("latest"));

    Log.Info($"Pushed ghcr.io/{Env("GITHUB_REPOSITORY_OWNER", required: false) ?? "owner"}/myapp:{version}");
}
```

Usage:
```bash
# Build and test only
ando

# Build, test, and push to ghcr.io
ando -p publish
```

## GitHub Actions Integration

Complete workflow for building and pushing on tag:

```yaml
# .github/workflows/release.yml
name: Release

on:
  push:
    tags:
      - 'v*'

jobs:
  build-and-push:
    runs-on: ubuntu-latest
    permissions:
      contents: read
      packages: write

    steps:
      - uses: actions/checkout@v4

      - name: Install ANDO
        run: dotnet tool install -g ando

      - name: Build and push container
        env:
          GITHUB_TOKEN: ${{ secrets.GITHUB_TOKEN }}
        run: ando -p publish
```

## Release with Container and Binaries

Combine container push with GitHub releases:

```csharp
var release = DefineProfile("release");
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
var version = project.Version;

// Build and test
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj"));

if (release)
{
    // Publish for container
    Dotnet.Publish(project, o => o
        .Output(Root / "publish")
        .WithConfiguration(Configuration.Release));

    // Build and push container
    Docker.Build("Dockerfile", o => o
        .WithTag($"myapp:{version}")
        .WithTag("myapp:latest")
        .WithBuildArg("VERSION", version));

    GitHub.PushImage("myapp", o => o.WithTag(version));
    GitHub.PushImage("myapp", o => o.WithTag("latest"));

    // Create GitHub release
    Git.Tag($"v{version}", o => o.WithSkipIfExists());
    Git.Push();
    Git.PushTags();

    GitHub.CreateRelease(o => o
        .WithTag($"v{version}")
        .WithTitle($"v{version}")
        .WithNotes($@"
## Container Image

```bash
docker pull ghcr.io/${{GITHUB_REPOSITORY}}:{version}
```

## What's Changed

See commit history for changes.
")
        .WithGeneratedNotes());
}
```

## Clean Builds

Force a fresh Docker build without cache:

```csharp
Docker.Build("Dockerfile", o => o
    .WithTag($"myapp:{version}")
    .WithNoCache());
```

Use `WithNoCache()` when:
- Debugging build issues
- Ensuring dependencies are updated
- Building release versions

## Options Reference

### Docker.Build Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Add image tag (can call multiple times) |
| `WithContext(string)` | Build context directory |
| `WithBuildArg(key, value)` | Pass build argument |
| `WithPlatform(string)` | Target platform (e.g., "linux/amd64") |
| `WithNoCache()` | Disable build cache |

### GitHub.PushImage Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Image tag to push |
| `WithOwner(string)` | GitHub user/org (auto-detected if not set) |

## See Also

- [Docker Provider](/providers/docker) - Docker operations reference
- [GitHub Provider](/providers/github) - GitHub operations reference
- [GitHub Releases Recipe](/recipes/github-releases) - Creating GitHub releases
