---
title: Docker
description: Build Docker container images using buildx.
provider: Docker
---

## Basic Usage

Build Docker images with static or dynamic tags. All builds use `docker buildx build` for future-proof compatibility.

```csharp
// Build a Docker image (loads into local docker)
Docker.Build("Dockerfile", o => o.WithTag("myapp:latest"));

// Build with explicit tag
Docker.Build("./src/MyApp/Dockerfile", o => o
    .WithTag("myapp:v1.0.0"));
```

## Advanced Options

Configure build arguments, platform, caching, and context directory.

```csharp
// Build with build arguments
Docker.Build("Dockerfile", o => o
    .WithTag("myapp:latest")
    .WithBuildArg("VERSION", "1.0.0")
    .WithBuildArg("BUILD_NUMBER", "123"));

// Specify platform (for cross-compilation)
Docker.Build("Dockerfile", o => o
    .WithTag("myapp:latest")
    .WithPlatform("linux/amd64"));

// Build without cache
Docker.Build("Dockerfile", o => o
    .WithTag("myapp:latest")
    .WithNoCache());

// Specify context directory
Docker.Build("./docker/Dockerfile", o => o
    .WithTag("myapp:latest")
    .WithContext("./src"));
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
    .WithTag("myapp:v1.0.0")
    .WithBuildArg("VERSION", "1.0.0")
    .WithContext("./src")
    .WithPush());
```

## Full Workflow Example

Build, push, and release a containerized application.

```csharp
// Build the application
Dotnet.Publish(app, o => o
    .Output(Root / "publish")
    .WithConfiguration(Configuration.Release));

// Build Docker image
Docker.Build("Dockerfile", o => o.WithTag("myapp:v1.0.0"));

// Push to GitHub Container Registry
GitHub.PushImage("myapp", o => o.WithTag("v1.0.0"));

// Create GitHub release
Git.Tag("v1.0.0");
Git.Push();
Git.PushTags();
GitHub.CreateRelease(o => o.WithTag("v1.0.0").WithGeneratedNotes());
```

## Options Reference

### Docker.Build Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Add an image tag. Can be called multiple times for multiple tags. Format: `name:tag` (e.g., "myapp:v1.0.0", "myapp:latest"). |
| `WithPlatform(string)` | Single target platform (e.g., "linux/amd64"). Clears any existing platforms. |
| `WithPlatforms(params string[])` | Multiple target platforms for multi-arch builds (e.g., "linux/amd64", "linux/arm64"). |
| `WithContext(string)` | Build context directory. This is the root directory for COPY instructions in the Dockerfile. Defaults to the Dockerfile's parent directory. |
| `WithBuildArg(key, value)` | Pass a build-time variable to the Dockerfile. Access in Dockerfile with `ARG key` and `${key}`. Common uses: version numbers, build timestamps, feature flags. |
| `WithPush()` | Push images to registry after building. Required for multi-platform builds. Disables `--load`. |
| `WithNoCache()` | Disable build cache entirely. Forces all layers to rebuild. Use when debugging caching issues or ensuring a clean build. |
| `WithoutLoad()` | Disable loading image into local docker. Useful when only pushing to a registry. |

## Notes

- Docker operations require Docker to be installed on the host machine.
- `Docker.Build` requires the `--dind` flag (Docker-in-Docker mode).
- For pushing images, use the registry-specific operations like [GitHub.PushImage](/providers/github#container-registry) or `WithPush()` for direct push.
- If the dockerfile parameter is a directory, ANDO assumes the Dockerfile is inside that directory.
- For multi-platform builds, ANDO automatically creates a buildx builder named `ando-builder` if one doesn't exist.
- `WithPush()` automatically handles ghcr.io authentication when pushing to GitHub Container Registry (uses `GITHUB_TOKEN` or gh CLI credentials). The owner is extracted from the ghcr.io tag (e.g., `ghcr.io/myorg/myapp` → owner is `myorg`).
