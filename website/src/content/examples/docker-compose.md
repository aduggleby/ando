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

## Basic Multi-Container Build

Build multiple images and run with Docker Compose:

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
Docker.Build("./src/Api", "myapp-api:latest");
Docker.Build("./src/Worker", "myapp-worker:latest");

// Run with Docker Compose
Docker.ComposeUp();

Log.Info("Application started with Docker Compose");
```

## Build and Push to Registry

Build images and push to a container registry:

```csharp
var deploy = DefineProfile("deploy");

var registry = "ghcr.io/myorg";
var version = Git.GetVersion();

// Build images with registry prefix
Docker.Build("./src/Api", $"{registry}/myapp-api:{version}");
Docker.Build("./src/Worker", $"{registry}/myapp-worker:{version}");

// Also tag as latest
Docker.Tag($"{registry}/myapp-api:{version}", $"{registry}/myapp-api:latest");
Docker.Tag($"{registry}/myapp-worker:{version}", $"{registry}/myapp-worker:latest");

if (deploy)
{
    // Push to registry
    GitHub.EnsureAuthenticated();
    Docker.Push($"{registry}/myapp-api:{version}");
    Docker.Push($"{registry}/myapp-api:latest");
    Docker.Push($"{registry}/myapp-worker:{version}");
    Docker.Push($"{registry}/myapp-worker:latest");

    Log.Info($"Pushed images with version {version}");
}
```

## Environment-Specific Compose Files

Use different compose files for different environments:

```csharp
var deploy = DefineProfile("deploy");
var production = DefineProfile("production");

// Build images
Docker.Build("./src/Api", "myapp-api:latest");
Docker.Build("./src/Worker", "myapp-worker:latest");

if (deploy)
{
    if (production)
    {
        // Production: use override file
        Docker.ComposeUp(o => o
            .WithFile("docker-compose.yml")
            .WithFile("docker-compose.prod.yml")
            .WithDetach());
    }
    else
    {
        // Development/staging
        Docker.ComposeUp(o => o
            .WithFile("docker-compose.yml")
            .WithDetach());
    }

    Log.Info("Compose stack deployed");
}
```

Usage:
```bash
# Deploy to staging
ando -p deploy

# Deploy to production
ando -p deploy,production
```

## With Database and Dependencies

Full stack with database, cache, and application services:

```csharp
var deploy = DefineProfile("deploy");

// Build application images
Docker.Build("./src/Api", "myapp-api:latest");
Docker.Build("./src/Worker", "myapp-worker:latest");

if (deploy)
{
    // Start infrastructure first
    Docker.ComposeUp(o => o
        .WithServices("postgres", "redis")
        .WithDetach()
        .WithWait());

    // Wait for database to be ready
    Docker.ComposeExec("postgres", "pg_isready -U postgres", o => o
        .WithRetry(30, 1000));

    // Run migrations
    Docker.ComposeRun("api", "dotnet ef database update");

    // Start application services
    Docker.ComposeUp(o => o
        .WithServices("api", "worker")
        .WithDetach());

    Log.Info("Full stack deployed");
}
```

## Health Checks and Rollback

Deploy with health checks and automatic rollback:

```csharp
var deploy = DefineProfile("deploy");

var version = Git.GetVersion();

Docker.Build("./src/Api", $"myapp-api:{version}");

if (deploy)
{
    // Pull current image as backup
    Docker.Pull("myapp-api:current");
    Docker.Tag("myapp-api:current", "myapp-api:rollback");

    // Tag new version as current
    Docker.Tag($"myapp-api:{version}", "myapp-api:current");

    // Deploy
    Docker.ComposeUp(o => o.WithDetach().WithRemoveOrphans());

    // Health check
    var healthy = Docker.ComposeHealthCheck("api", timeout: 60);

    if (!healthy)
    {
        Log.Error("Health check failed, rolling back...");
        Docker.Tag("myapp-api:rollback", "myapp-api:current");
        Docker.ComposeUp(o => o.WithDetach());
        throw new Exception("Deployment failed health check");
    }

    Log.Info($"Deployed version {version} successfully");
}
```

## Compose for Testing

Run integration tests with Docker Compose:

```csharp
var test = DefineProfile("test");

// Build test dependencies
Docker.Build("./src/Api", "myapp-api:test");

if (test)
{
    // Start test infrastructure
    Docker.ComposeUp(o => o
        .WithFile("docker-compose.test.yml")
        .WithDetach()
        .WithWait());

    // Run tests against compose services
    Dotnet.Test(Dotnet.Project("./tests/Integration.Tests/Integration.Tests.csproj"), o => o
        .WithEnvironment("API_URL", "http://localhost:5000"));

    // Cleanup
    Docker.ComposeDown(o => o
        .WithFile("docker-compose.test.yml")
        .WithVolumes());

    Log.Info("Integration tests complete");
}
```

## Example docker-compose.yml

```yaml
version: '3.8'

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

| Operation | Purpose |
|-----------|---------|
| [Docker.Build()](/providers/docker#build) | Build a Docker image |
| [Docker.ComposeUp()](/providers/docker#composeup) | Start compose services |
| [Docker.ComposeDown()](/providers/docker#composedown) | Stop compose services |
| [Docker.ComposeExec()](/providers/docker#composeexec) | Run command in service |
| [Docker.Push()](/providers/docker#push) | Push image to registry |

## Tips

- **Build context** - Keep Dockerfiles close to their source code
- **Multi-stage builds** - Use multi-stage Dockerfiles for smaller images
- **Named volumes** - Use named volumes for persistent data
- **Health checks** - Always define health checks for production

## See Also

- [Docker Provider](/providers/docker) - Full API reference
- [Container Registry](/examples/container-github-registry) - Push to registries
- [Playwright E2E Tests](/examples/playwright-e2e-tests) - Testing with containers
