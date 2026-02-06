---
title: Full Stack Deployment
description: Deploy a .NET API backend and JavaScript frontend to different hosting platforms.
category: Full Stack
tags:
  - dotnet
  - frontend
  - azure
  - cloudflare
  - full-stack
---

## Overview

Modern web applications often have separate backend and frontend codebases deployed to different platforms. This example shows how to build and deploy a .NET API to Azure App Service and a React/Vue frontend to Cloudflare Pages.

## Project Structure

```
my-app/
├── src/
│   └── Api/
│       └── Api.csproj          # .NET Web API
├── frontend/
│   ├── package.json            # React/Vue/Astro frontend
│   └── src/
├── tests/
│   └── Api.Tests/
└── build.csando
```

## Basic Full Stack Build

```csharp
var deploy = DefineProfile("deploy");

// Project references
var api = Dotnet.Project("./src/Api/Api.csproj");
var tests = Dotnet.Project("./tests/Api.Tests/Api.Tests.csproj");
var frontend = Directory("./frontend");

// === Backend ===
Dotnet.SdkInstall();
Dotnet.Restore(api);
Dotnet.Build(api);
Dotnet.Test(tests);

Dotnet.Publish(api, o => o
    .WithConfiguration(Configuration.Release)
    .Output(Root / "publish" / "api"));

// === Frontend ===
Node.Install();
Npm.Ci(frontend);
Npm.Build(frontend);

if (deploy)
{
    // Deploy API to Azure
    Azure.EnsureAuthenticated();
    AppService.DeployZip("my-api", Root / "publish" / "api");

    // Deploy frontend to Cloudflare
    Cloudflare.EnsureAuthenticated();
    Cloudflare.PagesDeploy(frontend / "dist", "my-frontend");
    Cloudflare.PurgeCache("example.com");

    Log.Info("Full stack deployment complete!");
}
```

## With Database Migrations

Include EF Core migrations before API deployment:

```csharp
var deploy = DefineProfile("deploy");

var api = Dotnet.Project("./src/Api/Api.csproj");
var frontend = Directory("./frontend");

// Build backend
Dotnet.SdkInstall();
Dotnet.Restore(api);
Dotnet.Build(api);
Dotnet.Publish(api, o => o
    .WithConfiguration(Configuration.Release)
    .Output(Root / "publish" / "api"));

// Build frontend
Node.Install();
Npm.Ci(frontend);
Npm.Build(frontend);

if (deploy)
{
    Azure.EnsureAuthenticated();

    // 1. Run database migrations first
    var dbContext = Ef.DbContextFrom(api, "AppDbContext");
    Ef.DatabaseUpdate(dbContext, Env("CONNECTION_STRING"));
    Log.Info("Database migrations applied");

    // 2. Deploy API with slot swap
    AppService.DeployWithSwap("my-api", Root / "publish" / "api", "staging");
    Log.Info("API deployed");

    // 3. Deploy frontend
    Cloudflare.EnsureAuthenticated();
    Cloudflare.PagesDeploy(frontend / "dist", "my-frontend");
    Cloudflare.PurgeCache("example.com");
    Log.Info("Frontend deployed");
}
```

## Separate Deployment Profiles

Deploy frontend and backend independently:

```csharp
var deployApi = DefineProfile("deploy-api");
var deployFrontend = DefineProfile("deploy-frontend");
var deployAll = DefineProfile("deploy");

var api = Dotnet.Project("./src/Api/Api.csproj");
var frontend = Directory("./frontend");

// Always build everything
Dotnet.SdkInstall();
Dotnet.Restore(api);
Dotnet.Build(api);
Dotnet.Publish(api, o => o
    .WithConfiguration(Configuration.Release)
    .Output(Root / "publish" / "api"));

Node.Install();
Npm.Ci(frontend);
Npm.Build(frontend);

// Deploy API
if (deployApi || deployAll)
{
    Azure.EnsureAuthenticated();
    AppService.DeployWithSwap("my-api", Root / "publish" / "api", "staging");
    Log.Info("API deployed");
}

// Deploy frontend
if (deployFrontend || deployAll)
{
    Cloudflare.EnsureAuthenticated();
    Cloudflare.PagesDeploy(frontend / "dist", "my-frontend");
    Cloudflare.PurgeCache("example.com");
    Log.Info("Frontend deployed");
}
```

Usage:
```bash
# Build only
ando

# Deploy just the API
ando -p deploy-api

# Deploy just the frontend
ando -p deploy-frontend

# Deploy everything
ando -p deploy
```

## With Environment-Specific Builds

Configure frontend for different environments:

```csharp
var deploy = DefineProfile("deploy");
var production = DefineProfile("production");

var api = Dotnet.Project("./src/Api/Api.csproj");
var frontend = Directory("./frontend");

// Build backend
Dotnet.SdkInstall();
Dotnet.Restore(api);
Dotnet.Build(api);

// Build frontend with environment-specific config
Node.Install();
Npm.Ci(frontend);

if (production)
{
    // Production build with optimizations
    Npm.Run(frontend, "build:production");
}
else
{
    // Staging/development build
    Npm.Run(frontend, "build:staging");
}

if (deploy)
{
    var appName = production ? "my-api-prod" : "my-api-staging";
    var pagesProject = production ? "my-frontend-prod" : "my-frontend-staging";

    Azure.EnsureAuthenticated();
    Dotnet.Publish(api, o => o.Output(Root / "publish" / "api"));
    AppService.DeployZip(appName, Root / "publish" / "api");

    Cloudflare.EnsureAuthenticated();
    Cloudflare.PagesDeploy(frontend / "dist", pagesProject);
}
```

Usage:
```bash
# Deploy to staging
ando -p deploy

# Deploy to production
ando -p deploy,production
```

## Full Example with Tests

Complete full-stack pipeline with testing:

```csharp
var deploy = DefineProfile("deploy");

var api = Dotnet.Project("./src/Api/Api.csproj");
var apiTests = Dotnet.Project("./tests/Api.Tests/Api.Tests.csproj");
var frontend = Directory("./frontend");

// === Backend ===
Dotnet.SdkInstall();
Dotnet.Restore(api);
Dotnet.Build(api);
Dotnet.Test(apiTests);

Dotnet.Publish(api, o => o
    .WithConfiguration(Configuration.Release)
    .Output(Root / "publish" / "api"));

// === Frontend ===
Node.Install();
Npm.Ci(frontend);

// Run frontend tests
Npm.Test(frontend);

// Build for production
Npm.Build(frontend);

// === Deploy ===
if (deploy)
{
    // Database migrations
    Azure.EnsureAuthenticated();
    var dbContext = Ef.DbContextFrom(api, "AppDbContext");
    Ef.DatabaseUpdate(dbContext, Env("CONNECTION_STRING"));

    // Deploy API
    AppService.DeployWithSwap("my-api", Root / "publish" / "api", "staging");

    // Deploy frontend
    Cloudflare.EnsureAuthenticated();
    Cloudflare.PagesDeploy(frontend / "dist", "my-frontend");
    Cloudflare.PurgeCache("example.com");

    Log.Info("Full stack deployment complete!");
}

// Copy artifacts for local inspection
Ando.CopyArtifactsToHost("publish", "./dist");
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Dotnet.Publish()](/providers/dotnet#publish) | Build .NET for deployment |
| [Npm.Build()](/providers/npm#build) | Build frontend assets |
| [AppService.DeployZip()](/providers/appservice#deployzip) | Deploy to Azure App Service |
| [Cloudflare.PagesDeploy()](/providers/cloudflare#pagesdeploy) | Deploy to Cloudflare Pages |

## See Also

- [Azure App Service](/examples/azure-app-service) - Backend deployment details
- [Astro + Cloudflare](/examples/astro) - Frontend deployment details
- [EF Core Migrations](/examples/ef-migrations) - Database migrations
