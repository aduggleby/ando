---
title: Docker to GitHub Registry
description: Build Docker images and push to GitHub Container Registry (ghcr.io).
category: Docker
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

## Recommended: Atomic Build and Push

**Best practice**: Use `Docker.Build` with `WithPush()` to build and push in a single atomic operation. This ensures:
- Both version and latest tags point to the same manifest
- No cache staleness issues between build and push
- Multi-platform support works correctly
- Simpler, more reliable builds

```csharp
var version = "1.0.0";

// Build and push in one atomic operation
Docker.Install();
Docker.Build("Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag($"ghcr.io/my-org/myapp:{version}")
    .WithTag("ghcr.io/my-org/myapp:latest")
    .WithPush());
```

The image will be available at `ghcr.io/my-org/myapp:1.0.0` and `ghcr.io/my-org/myapp:latest`, with both tags pointing to identical multi-arch manifests.

## Why Atomic Build+Push?

Using `WithPush()` leverages buildx's `--push` flag to atomically build and push:

- All tags point to the same manifest (no cache staleness)
- Multi-platform builds work correctly
- Single operation, fewer network round-trips
- Automatic ghcr.io authentication

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

// Build and push multi-arch image atomically
Docker.Install();
Docker.Build("Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag($"ghcr.io/my-org/myapp:{version}")
    .WithTag("ghcr.io/my-org/myapp:latest")
    .WithPush());
```

### Dockerfile Example

```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app
COPY publish/ .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MyApp.dll"]
```

## Build Arguments

Inject version numbers or build metadata into the image:

```csharp
var version = "1.0.0";
var buildNumber = Env("BUILD_NUMBER", required: false) ?? "local";

Docker.Build("Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag($"ghcr.io/my-org/myapp:{version}")
    .WithBuildArg("VERSION", version)
    .WithBuildArg("BUILD_NUMBER", buildNumber)
    .WithPush());
```

Access in Dockerfile:
```dockerfile
ARG VERSION
ARG BUILD_NUMBER
ENV APP_VERSION=${VERSION}
ENV APP_BUILD=${BUILD_NUMBER}
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

// Always publish for container (for verification)
Dotnet.Publish(project, o => o
    .Output(Root / "publish")
    .WithConfiguration(Configuration.Release));

// Only build and push container with -p publish
if (publish)
{
    Docker.Install();
    Docker.Build("Dockerfile", o => o
        .WithPlatforms("linux/amd64", "linux/arm64")
        .WithTag($"ghcr.io/my-org/myapp:{version}")
        .WithTag("ghcr.io/my-org/myapp:latest")
        .WithPush());

    Log.Info($"Pushed ghcr.io/my-org/myapp:{version}");
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

    // Build and push container atomically
    Docker.Install();
    Docker.Build("Dockerfile", o => o
        .WithPlatforms("linux/amd64", "linux/arm64")
        .WithTag($"ghcr.io/my-org/myapp:{version}")
        .WithTag("ghcr.io/my-org/myapp:latest")
        .WithBuildArg("VERSION", version)
        .WithPush());

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
docker pull ghcr.io/my-org/myapp:{version}
```

## What's Changed

See commit history for changes.
")
        .WithGeneratedNotes());
}
```

## Single Platform Builds

For simpler use cases without multi-arch requirements:

```csharp
// Single platform build with push
Docker.Build("Dockerfile", o => o
    .WithPlatform("linux/amd64")
    .WithTag($"ghcr.io/my-org/myapp:{version}")
    .WithPush());
```

## Local Development (Build Only)

For local testing, build without pushing:

```csharp
// Build and load into local Docker (no push)
Docker.Build("Dockerfile", o => o
    .WithTag("myapp:dev"));
```

## Clean Builds

Force a fresh Docker build without cache:

```csharp
Docker.Build("Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag($"ghcr.io/my-org/myapp:{version}")
    .WithNoCache()
    .WithPush());
```

Use `WithNoCache()` when:
- Debugging build issues
- Ensuring dependencies are updated
- Building release versions

## Options Reference

### Docker.Build Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Add image tag (can call multiple times). Use full registry path for push (e.g., `ghcr.io/org/app:v1`) |
| `WithPlatform(string)` | Single target platform (e.g., "linux/amd64") |
| `WithPlatforms(params string[])` | Multiple target platforms for multi-arch builds (e.g., "linux/amd64", "linux/arm64") |
| `WithContext(string)` | Build context directory |
| `WithBuildArg(key, value)` | Pass build argument |
| `WithPush()` | Push images to registry after building (recommended) |
| `WithNoCache()` | Disable build cache |

## See Also

- [Docker Provider](/providers/docker) - Docker operations reference
- [GitHub Provider](/providers/github) - GitHub operations reference
- [GitHub Releases Recipe](/recipes/github-releases) - Creating GitHub releases
