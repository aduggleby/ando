---
title: Docker
description: Build Docker container images using the Docker CLI.
provider: Docker
---

## Basic Usage

Build Docker images with static or dynamic tags.

```csharp
// Build a Docker image
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
| `WithTag(string)` | Add an image tag. Can be called multiple times to add multiple tags. Format: `name:tag` (e.g., "myapp:v1.0.0", "myapp:latest"). |
| `WithContext(string)` | Build context directory. This is the root directory for COPY instructions in the Dockerfile. Defaults to the Dockerfile's parent directory. |
| `WithBuildArg(key, value)` | Pass a build-time variable to the Dockerfile. Access in Dockerfile with `ARG key` and `${key}`. Common uses: version numbers, build timestamps, feature flags. |
| `WithPlatform(string)` | Target platform for cross-compilation. Format: `os/arch` (e.g., "linux/amd64", "linux/arm64"). Required when building for a different architecture than your host. |
| `WithNoCache()` | Disable build cache entirely. Forces all layers to rebuild. Use when debugging caching issues or ensuring a clean build. |

## Multi-Architecture Builds with Buildx

Use `Docker.Buildx` to build images for multiple platforms in a single command. This is particularly useful for ARM64 support (Apple Silicon, AWS Graviton) alongside AMD64.

```csharp
// Build for multiple platforms and push to registry
Docker.Install();
Docker.Buildx("./Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag("ghcr.io/myorg/myapp:v1.0.0")
    .WithTag("ghcr.io/myorg/myapp:latest")
    .WithPush());
```

**Note:** Multi-architecture builds with `--push` require the images to be pushed directly to a registry (they cannot be loaded into the local Docker daemon when building for multiple platforms).

### Buildx with Build Arguments

```csharp
Docker.Buildx("./src/MyApp/Dockerfile", o => o
    .WithPlatforms("linux/amd64", "linux/arm64")
    .WithTag("myapp:v1.0.0")
    .WithBuildArg("VERSION", "1.0.0")
    .WithContext("./src")
    .WithPush());
```

## Options Reference

### Docker.Build Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Add an image tag. Can be called multiple times to add multiple tags. Format: `name:tag` (e.g., "myapp:v1.0.0", "myapp:latest"). |
| `WithContext(string)` | Build context directory. This is the root directory for COPY instructions in the Dockerfile. Defaults to the Dockerfile's parent directory. |
| `WithBuildArg(key, value)` | Pass a build-time variable to the Dockerfile. Access in Dockerfile with `ARG key` and `${key}`. Common uses: version numbers, build timestamps, feature flags. |
| `WithPlatform(string)` | Target platform for cross-compilation. Format: `os/arch` (e.g., "linux/amd64", "linux/arm64"). Required when building for a different architecture than your host. |
| `WithNoCache()` | Disable build cache entirely. Forces all layers to rebuild. Use when debugging caching issues or ensuring a clean build. |

### Docker.Buildx Options

| Option | Description |
|--------|-------------|
| `WithTag(string)` | Add an image tag. Can be called multiple times for multiple tags. |
| `WithPlatforms(params string[])` | Target platforms (e.g., "linux/amd64", "linux/arm64"). Builds for all specified platforms. |
| `WithContext(string)` | Build context directory. Defaults to the Dockerfile's parent directory. |
| `WithBuildArg(key, value)` | Pass a build-time variable to the Dockerfile. |
| `WithPush()` | Push images to registry after building. Required for multi-platform builds. |
| `WithNoCache()` | Disable build cache entirely. |

## Notes

- Docker operations require Docker to be installed on the host machine.
- `Docker.Build` and `Docker.Buildx` require the `--dind` flag (Docker-in-Docker mode).
- For pushing images, use the registry-specific operations like [GitHub.PushImage](/providers/github#container-registry).
- If the dockerfile parameter is a directory, ANDO assumes the Dockerfile is inside that directory.
- `Docker.Buildx` automatically creates a buildx builder named `ando-builder` if one doesn't exist.
