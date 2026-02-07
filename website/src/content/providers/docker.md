---
title: Docker
description: Build Docker container images using buildx.
provider: Docker
---

## Recommended: Atomic Build and Push

**Best practice**: Use `Docker.Build` with `WithPush()` to build and push in a single atomic operation. This ensures:
- Both version and latest tags point to the same manifest
- No cache staleness issues between build and push
- Multi-platform support works correctly

```csharp
// Build multi-arch image and push atomically
Docker.Install();
Docker.Build("./Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithTag("ghcr.io/myorg/myapp:latest")
    .WithPush());
```

## Basic Usage

Build Docker images with static or dynamic tags. All builds use `docker buildx build` for future-proof compatibility.

```csharp
// Build a Docker image (loads into local docker for testing)
Docker.Build("Dockerfile", o => o.WithTag("myapp:dev"));

// Build with explicit tag and push to registry
Docker.Build("./src/MyApp/Dockerfile", o => o
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithPush());
```

## Advanced Options

Configure build arguments, platform, caching, and context directory.

```csharp
// Build with build arguments
Docker.Build("Dockerfile", o => o
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithBuildArg("VERSION", "1.0.0")
    .WithBuildArg("BUILD_NUMBER", "123")
    .WithPush());

// Specify platform (for cross-compilation)
Docker.Build("Dockerfile", o => o
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithPlatform("linux/amd64")
    .WithPush());

// Build without cache
Docker.Build("Dockerfile", o => o
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithNoCache()
    .WithPush());

// Specify context directory
Docker.Build("./docker/Dockerfile", o => o
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithContext("./src")
    .WithPush());
```

## Multi-Architecture Builds

Build images for multiple platforms in a single command. This is useful for ARM64 support (Apple Silicon, AWS Graviton) alongside AMD64.

```csharp
// Build for multiple platforms and push to registry
Docker.Install();
Docker.Build("./Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithTag("ghcr.io/myorg/myapp:latest")
    .WithPush());
```

**Note:** Multi-architecture builds with `WithPush()` require images to be pushed directly to a registry (they cannot be loaded into the local Docker daemon when building for multiple platforms).

### Multi-Platform with Build Arguments

```csharp
Docker.Build("./src/MyApp/Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithBuildArg("VERSION", "1.0.0")
    .WithContext("./src")
    .WithPush());
```

## Full Workflow Example

Build, push, and release a containerized application.

```csharp
var version = "1.0.0";

// Build the .NET application
Dotnet.Publish(app, o => o
    .Output(Root / "publish")
    .WithConfiguration(Configuration.Release));

// Build multi-arch image and push atomically
Docker.Install();
Docker.Build("Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag($"ghcr.io/myorg/myapp:{version}")
    .WithTag("ghcr.io/myorg/myapp:latest")
    .WithPush());

// Create GitHub release
Git.Tag($"v{version}");
Git.Push();
Git.PushTags();
GitHub.CreateRelease(o => o.WithTag($"v{version}").WithGeneratedNotes());
```

## Local Development

For local testing without pushing to a registry:

```csharp
// Build and load into local docker (no push)
Docker.Build("Dockerfile", o => o.WithTag("myapp:dev"));
```

## Options Reference

### Docker.Build Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Add an image tag. Can be called multiple times for multiple tags. Use full registry path for push (e.g., "ghcr.io/org/app:v1"). |
| `WithPlatform(string)` | Single target platform (e.g., "linux/amd64"). Clears any existing platforms. |
| `WithPlatforms(params string[])` | Multiple target platforms for multi-arch builds (e.g., "linux/amd64", "linux/arm64"). |
| `WithContext(string)` | Build context directory. This is the root directory for COPY instructions in the Dockerfile. Defaults to the Dockerfile's parent directory. |
| `WithBuildArg(key, value)` | Pass a build-time variable to the Dockerfile. Access in Dockerfile with `ARG key` and `${key}`. Common uses: version numbers, build timestamps, feature flags. |
| `WithPush()` | **Recommended**: Push images to registry atomically during build. Ensures all tags point to the same manifest. Required for multi-platform builds. |
| `WithNoCache()` | Disable build cache entirely. Forces all layers to rebuild. Use when debugging caching issues or ensuring a clean build. |
| `WithoutLoad()` | Disable loading image into local docker. Useful when only pushing to a registry. |

## Notes

- Docker operations require Docker to be installed on the host machine.
- `Docker.Build` requires the `--dind` flag (Docker-in-Docker mode).
- **CI Server**: When running on the ANDO CI Server, the Docker CLI is automatically installed in DIND build containers. `Docker.Install()` is still recommended in build scripts for portability (it is a no-op if Docker is already present).
- Use `WithPush()` for atomic build+push to ensure all tags point to the same manifest.
- If the dockerfile parameter is a directory, ANDO assumes the Dockerfile is inside that directory.
- For multi-platform builds, ANDO automatically creates a buildx builder named `ando-builder` if one doesn't exist.
- `WithPush()` automatically handles ghcr.io authentication when pushing to GitHub Container Registry (uses `GITHUB_TOKEN` or gh CLI credentials). The owner is extracted from the ghcr.io tag (e.g., `ghcr.io/myorg/myapp` → owner is `myorg`).
