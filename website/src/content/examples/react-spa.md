---
title: React/Vue SPA
description: Build and deploy single-page applications with Vite, React, or Vue.
category: Frontend
tags:
  - frontend
  - react
  - vue
  - vite
  - spa
---

## Overview

Single-page applications (SPAs) built with React, Vue, or other frameworks typically use Vite for fast development and optimized production builds. This example shows how to build and deploy SPAs with ANDO.

## Project Structure

```
my-spa/
├── src/
│   ├── App.tsx
│   ├── main.tsx
│   └── components/
├── public/
├── index.html
├── package.json
├── vite.config.ts
└── build.csando
```

## Basic React/Vue Build

Build a Vite-based SPA:

```csharp
var deploy = DefineProfile("deploy");

var frontend = Directory("./");

// Install dependencies
Node.Install();
Npm.Ci(frontend);

// Run linting and type checking
Npm.Run(frontend, "lint");
Npm.Run(frontend, "typecheck");

// Run tests
Npm.Test(frontend);

// Build for production
Npm.Build(frontend);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Deploy to Cloudflare Pages
    Cloudflare.PagesDeploy(frontend / "dist", "my-spa");

    Log.Info("SPA deployed to Cloudflare Pages");
}
```

## With Environment Variables

Configure environment-specific builds:

```csharp
var deploy = DefineProfile("deploy");
var production = DefineProfile("production");

var frontend = Directory("./");

Node.Install();
Npm.Ci(frontend);

// Set environment for build
if (production)
{
    Environment.SetEnvironmentVariable("VITE_API_URL", "https://api.example.com");
    Environment.SetEnvironmentVariable("VITE_ENV", "production");
}
else
{
    Environment.SetEnvironmentVariable("VITE_API_URL", "https://api-staging.example.com");
    Environment.SetEnvironmentVariable("VITE_ENV", "staging");
}

// Build with environment variables
Npm.Build(frontend);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    var projectName = production ? "my-spa" : "my-spa-staging";
    Cloudflare.PagesDeploy(frontend / "dist", projectName);

    Log.Info($"Deployed to {projectName}");
}
```

Usage:
```bash
# Build and deploy to staging
ando -p deploy

# Build and deploy to production
ando -p deploy,production
```

## Deploy to Azure Static Web Apps

Deploy SPA to Azure Static Web Apps:

```csharp
var deploy = DefineProfile("deploy");

var frontend = Directory("./");

Node.Install();
Npm.Ci(frontend);
Npm.Test(frontend);
Npm.Build(frontend);

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Deploy to Azure Static Web Apps
    Azure.StaticWebAppsDeploy(
        frontend / "dist",
        "my-static-app",
        Env("AZURE_STATIC_WEB_APPS_API_TOKEN")
    );

    Log.Info("Deployed to Azure Static Web Apps");
}
```

## Deploy to AWS S3 + CloudFront

Deploy SPA to S3 with CloudFront CDN:

```csharp
var deploy = DefineProfile("deploy");

var frontend = Directory("./");

Node.Install();
Npm.Ci(frontend);
Npm.Test(frontend);
Npm.Build(frontend);

if (deploy)
{
    // Sync to S3
    Aws.S3Sync(frontend / "dist", "s3://my-spa-bucket");

    // Invalidate CloudFront cache
    Aws.CloudFrontInvalidate("E1234567890", "/*");

    Log.Info("Deployed to AWS S3 + CloudFront");
}
```

## With Backend API

Build SPA alongside a .NET API:

```csharp
var deploy = DefineProfile("deploy");

var api = Dotnet.Project("./api/Api.csproj");
var frontend = Directory("./frontend");

// Build API
Dotnet.SdkInstall();
Dotnet.Restore(api);
Dotnet.Build(api);
Dotnet.Test(Dotnet.Project("./api.tests/Api.Tests.csproj"));

// Build frontend
Node.Install();
Npm.Ci(frontend);
Npm.Test(frontend);
Npm.Build(frontend);

if (deploy)
{
    // Deploy API to Azure
    Azure.EnsureAuthenticated();
    Dotnet.Publish(api, o => o.Output(Root / "publish" / "api"));
    AppService.DeployZip("my-api", Root / "publish" / "api");

    // Deploy frontend to Cloudflare
    Cloudflare.EnsureAuthenticated();
    Cloudflare.PagesDeploy(frontend / "dist", "my-spa");

    Log.Info("Full stack deployed");
}
```

## E2E Testing with Playwright

Run Playwright tests before deployment:

```csharp
var deploy = DefineProfile("deploy");
var e2e = DefineProfile("e2e");

var frontend = Directory("./");

Node.Install();
Npm.Ci(frontend);
Npm.Test(frontend);
Npm.Build(frontend);

if (e2e || deploy)
{
    // Start preview server and run E2E tests
    Playwright.Test(frontend, o => o
        .WithProject("chromium")
        .WithBaseUrl("http://localhost:4173"));

    Log.Info("E2E tests passed");
}

if (deploy)
{
    Cloudflare.EnsureAuthenticated();
    Cloudflare.PagesDeploy(frontend / "dist", "my-spa");
    Log.Info("Deployed after E2E verification");
}
```

## Monorepo with Turborepo/Nx

Build SPA in a monorepo setup:

```csharp
var deploy = DefineProfile("deploy");

var monorepo = Directory("./");

Node.Install();
Npm.Ci(monorepo);

// Build all packages using Turborepo
Npm.Run(monorepo, "build");

// Or build specific app
Npm.Run(monorepo, "build", "--filter=web");

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Deploy the web app
    Cloudflare.PagesDeploy(monorepo / "apps" / "web" / "dist", "my-spa");

    Log.Info("Deployed web app from monorepo");
}
```

## Docker Containerized SPA

Build SPA as a Docker container with nginx:

```csharp
var deploy = DefineProfile("deploy");

var frontend = Directory("./");
var registry = "ghcr.io/myorg";
var version = Git.GetVersion();

// Build frontend
Node.Install();
Npm.Ci(frontend);
Npm.Test(frontend);
Npm.Build(frontend);

// Build Docker image (Dockerfile serves dist/ via nginx)
Docker.Build(frontend, $"{registry}/my-spa:{version}");

if (deploy)
{
    GitHub.EnsureAuthenticated();

    // Push to registry
    Docker.Push($"{registry}/my-spa:{version}");
    Docker.Push($"{registry}/my-spa:latest");

    Log.Info($"Pushed container image {version}");
}
```

Example `Dockerfile`:
```dockerfile
FROM node:20-alpine AS builder
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
COPY --from=builder /app/dist /usr/share/nginx/html
COPY nginx.conf /etc/nginx/conf.d/default.conf
EXPOSE 80
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Npm.Ci()](/providers/npm#ci) | Install dependencies (CI mode) |
| [Npm.Build()](/providers/npm#build) | Build for production |
| [Npm.Test()](/providers/npm#test) | Run unit tests |
| [Cloudflare.PagesDeploy()](/providers/cloudflare#pagesdeploy) | Deploy to Cloudflare |
| [Playwright.Test()](/providers/playwright#test) | Run E2E tests |

## Tips

- **Use npm ci** - Faster and more reliable than `npm install` in CI
- **Environment variables** - Use `VITE_*` prefix for client-side env vars
- **Optimize bundle** - Enable tree shaking and code splitting
- **Cache dependencies** - Cache `node_modules` between builds if possible

## See Also

- [Npm Provider](/providers/npm) - Full API reference
- [Astro + Cloudflare](/examples/astro) - SSG deployment
- [Playwright E2E Tests](/examples/playwright-e2e-tests) - Testing guide
- [Full Stack Deployment](/examples/fullstack-deploy) - SPA + API
