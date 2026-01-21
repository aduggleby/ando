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

## Notes

- Docker operations require Docker to be installed on the host machine.
- For pushing images, use the registry-specific operations like [GitHub.PushImage](/providers/github#container-registry).
- If the dockerfile parameter is a directory, ANDO assumes the Dockerfile is inside that directory.
