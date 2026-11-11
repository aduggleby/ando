---
title: Docker Compose
description: Build and deploy multi-container applications with Docker Compose.
category: Docker
tags:
  - docker
  - docker-compose
  - containers
  - multi-container
---

## Overview

Docker Compose enables you to define and run multi-container applications. This example shows how to build container images and manage Docker Compose deployments with ANDO.

## Project Structure

```
my-app/
├── src/
│   ├── Api/
│   │   ├── Api.csproj
│   │   └── Dockerfile
│   └── Worker/
│       ├── Worker.csproj
│       └── Dockerfile
├── docker-compose.yml
├── docker-compose.prod.yml      # Production overrides
└── build.csando
```

## How Compose fits in

ANDO builds and pushes images with `Docker.Build`. It has no Docker Compose operation, so the compose commands below use `Shell.Run`, the escape hatch for tools without an ANDO operation. They need the `--dind` flag (`ando run --dind`), `Docker.Install()` first, and a `docker compose` plugin in the build container image.

## Basic Multi-Container Build

Build multiple images and run them with Docker Compose:

```csharp
var api = Dotnet.Project("./src/Api/Api.csproj");
var worker = Dotnet.Project("./src/Worker/Worker.csproj");

// Build .NET projects
Dotnet.SdkInstall();
Dotnet.Restore(api);
Dotnet.Restore(worker);
Dotnet.Build(api);
Dotnet.Build(worker);

// Build Docker images
Docker.Install();
Docker.Build("./src/Api/Dockerfile", o => o.WithTag("myapp-api:latest"));
Docker.Build("./src/Worker/Dockerfile", o => o.WithTag("myapp-worker:latest"));

// Run with Docker Compose
Shell.Run("docker", "compose", "up", "-d");

Log.Info("Application started with Docker Compose");
```

## Build and Push to Registry

Build images and push them to a container registry. `WithPush()` builds and pushes in one step, so the version and `latest` tags point to the same manifest:

```csharp
var deploy = DefineProfile("deploy");

var registry = "ghcr.io/myorg";
var version = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

Docker.Install();

if (deploy)
{
    Docker.Build("./src/Api/Dockerfile", o => o
        .WithTag($"{registry}/myapp-api:{version}")
        .WithTag($"{registry}/myapp-api:latest")
        .WithPush());
    Docker.Build("./src/Worker/Dockerfile", o => o
        .WithTag($"{registry}/myapp-worker:{version}")
        .WithTag($"{registry}/myapp-worker:latest")
        .WithPush());

    Log.Info($"Pushed images with version {version}");
}
else
{
    // Local build only
    Docker.Build("./src/Api/Dockerfile", o => o.WithTag($"{registry}/myapp-api:{version}"));
    Docker.Build("./src/Worker/Dockerfile", o => o.WithTag($"{registry}/myapp-worker:{version}"));
}
```

See [Container Registry](/examples/container-github-registry) for registry authentication.

## Environment-Specific Compose Files

Use different compose files for different environments:

```csharp
var deploy = DefineProfile("deploy");
var production = DefineProfile("production");

Docker.Install();
Docker.Build("./src/Api/Dockerfile", o => o.WithTag("myapp-api:latest"));
Docker.Build("./src/Worker/Dockerfile", o => o.WithTag("myapp-worker:latest"));

if (deploy)
{
    if (production)
    {
        // Production: use override file
        Shell.Run("docker", "compose", "-f", "docker-compose.yml", "-f", "docker-compose.prod.yml", "up", "-d");
    }
    else
    {
        // Development/staging
        Shell.Run("docker", "compose", "-f", "docker-compose.yml", "up", "-d");
    }

    Log.Info("Compose stack deployed");
}
```

Usage:

```bash
# Deploy to staging
ando -p deploy --dind

# Deploy to production
ando -p deploy,production --dind
```

## With Database and Dependencies

Full stack with database, cache, and application services:

```csharp
var deploy = DefineProfile("deploy");

Docker.Install();
Docker.Build("./src/Api/Dockerfile", o => o.WithTag("myapp-api:latest"));
Docker.Build("./src/Worker/Dockerfile", o => o.WithTag("myapp-worker:latest"));

if (deploy)
{
    // Start infrastructure first and wait for the health checks defined in docker-compose.yml
    Shell.Run("docker", "compose", "up", "-d", "--wait", "postgres", "redis");

    // Run migrations
    Shell.Run("docker", "compose", "run", "--rm", "api", "dotnet", "ef", "database", "update");

    // Start application services
    Shell.Run("docker", "compose", "up", "-d", "api", "worker");

    Log.Info("Full stack deployed");
}
```

## Health Checks and Rollback

Deploy and roll back when the health check fails. `Ando.Block` runs its `OnFailure` steps when a step inside it fails:

```csharp
var deploy = DefineProfile("deploy");

var version = Git.LastTag("v*")?.TrimStart('v') ?? "0.0.0";

Docker.Install();
Docker.Build("./src/Api/Dockerfile", o => o.WithTag($"myapp-api:{version}"));

if (deploy)
{
    Ando.Block("deploy", () =>
    {
        // Keep the running image as a rollback target, then promote the new one
        Shell.Run("docker", "tag", "myapp-api:current", "myapp-api:rollback").AllowFailure();
        Shell.Run("docker", "tag", $"myapp-api:{version}", "myapp-api:current");

        Shell.Run("docker", "compose", "up", "-d", "--wait", "--remove-orphans");
    })
    .OnFailure(() =>
    {
        Log.Warning("Deploy failed, rolling back...");
        Shell.Run("docker", "tag", "myapp-api:rollback", "myapp-api:current");
        Shell.Run("docker", "compose", "up", "-d");
    });

    Log.Info($"Deployed version {version}");
}
```

`--wait` makes `docker compose up` fail when a service's health check does not pass, which triggers the `OnFailure` steps. The block still fails the build after the rollback ran.

## Compose for Testing

Run integration tests against Docker Compose services:

```csharp
var test = DefineProfile("test");

Docker.Install();
Docker.Build("./src/Api/Dockerfile", o => o.WithTag("myapp-api:test"));

if (test)
{
    Ando.Block("integration", () =>
    {
        Shell.Run("docker", "compose", "-f", "docker-compose.test.yml", "up", "-d", "--wait");

        // The tests call the services started above
        Dotnet.Test(Dotnet.Project("./tests/Integration.Tests/Integration.Tests.csproj"));
    })
    .Finally(() => Shell.Run("docker", "compose", "-f", "docker-compose.test.yml", "down", "--volumes"));

    Log.Info("Integration tests complete");
}
```

`Dotnet.Test` has no way to set environment variables for one run. Have the tests read the compose service URL from `appsettings` or from an environment variable set in `.env.ando`.

## Example docker-compose.yml

```yaml
version: "3.8"

services:
  api:
    image: myapp-api:latest
    ports:
      - "5000:80"
    environment:
      - ConnectionStrings__Default=Host=postgres;Database=myapp;Username=postgres;Password=secret
      - Redis__Host=redis
    depends_on:
      - postgres
      - redis
    healthcheck:
      test: ["CMD", "curl", "-f", "http://localhost/health"]
      interval: 10s
      timeout: 5s
      retries: 3

  worker:
    image: myapp-worker:latest
    environment:
      - ConnectionStrings__Default=Host=postgres;Database=myapp;Username=postgres;Password=secret
      - Redis__Host=redis
    depends_on:
      - postgres
      - redis

  postgres:
    image: postgres:15
    environment:
      - POSTGRES_PASSWORD=secret
      - POSTGRES_DB=myapp
    volumes:
      - postgres_data:/var/lib/postgresql/data

  redis:
    image: redis:7-alpine

volumes:
  postgres_data:
```

## Key Operations

| Operation                                     | Purpose                                                     |
| --------------------------------------------- | ----------------------------------------------------------- |
| [Docker.Install()](/providers/docker#install) | Install the Docker CLI in the container                     |
| [Docker.Build()](/providers/docker#build)     | Build (and optionally push) a Docker image                  |
| [Shell.Run()](/providers/shell#run)           | Run `docker compose` commands (no Compose operation exists) |
| [Ando.Block()](/providers/ando#block)         | Roll back with `OnFailure`, clean up with `Finally`         |

## Tips

- **Build context** - Keep Dockerfiles close to their source code
- **Multi-stage builds** - Use multi-stage Dockerfiles for smaller images
- **Named volumes** - Use named volumes for persistent data
- **Health checks** - Always define health checks for production

## See Also

- [Docker Provider](/providers/docker) - Full API reference
- [Container Registry](/examples/container-github-registry) - Push to registries
- [Playwright E2E Tests](/examples/playwright-e2e-tests) - Testing with containers
