---
title: Monorepo Build
description: Orchestrate builds across multiple projects in a monorepo using Ando.Build.
category: .NET
tags:
  - dotnet
  - monorepo
  - multi-project
  - orchestration
---

## Overview

Large codebases often organize multiple projects in a monorepo. This example shows how to use `Ando.Build()` to orchestrate builds across sub-projects, each with their own `build.csando` file.

## Project Structure

```
my-monorepo/
├── packages/
│   ├── core/
│   │   ├── src/Core/Core.csproj
│   │   └── build.csando
│   ├── api/
│   │   ├── src/Api/Api.csproj
│   │   └── build.csando
│   └── web/
│       ├── package.json
│       └── build.csando
├── shared/
│   └── Common/Common.csproj
└── build.csando                  # Root orchestrator
```

## Basic Monorepo Build

Orchestrate multiple sub-projects from the root:

```csharp
// Root build.csando

var core = Directory("./packages/core");
var api = Directory("./packages/api");
var web = Directory("./packages/web");

// Build in dependency order
Ando.Build(core);
Ando.Build(api);
Ando.Build(web);

Log.Info("All packages built successfully");
```

Each sub-project has its own `build.csando`:

```csharp
// packages/core/build.csando

var project = Dotnet.Project("./src/Core/Core.csproj");

Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/Core.Tests/Core.Tests.csproj"));
```

## Pass Profiles to Sub-Builds

Share profiles across the monorepo:

```csharp
// Root build.csando

var deploy = DefineProfile("deploy");
var production = DefineProfile("production");

var core = Directory("./packages/core");
var api = Directory("./packages/api");
var web = Directory("./packages/web");

// Build all packages
Ando.Build(core);
Ando.Build(api);
Ando.Build(web);

// Deploy all if profile is active
if (deploy)
{
    // Profiles are automatically passed to sub-builds
    Ando.Build(api, o => o.WithProfiles("deploy"));
    Ando.Build(web, o => o.WithProfiles("deploy"));

    if (production)
    {
        Ando.Build(api, o => o.WithProfiles("deploy", "production"));
        Ando.Build(web, o => o.WithProfiles("deploy", "production"));
    }

    Log.Info("All packages deployed");
}
```

Usage:
```bash
# Build only
ando

# Deploy to staging
ando -p deploy

# Deploy to production
ando -p deploy,production
```

## Conditional Sub-Builds

Only build changed packages:

```csharp
var buildCore = DefineProfile("core");
var buildApi = DefineProfile("api");
var buildWeb = DefineProfile("web");
var buildAll = !buildCore && !buildApi && !buildWeb;

var core = Directory("./packages/core");
var api = Directory("./packages/api");
var web = Directory("./packages/web");

// Build based on profiles
if (buildCore || buildAll)
{
    Ando.Build(core);
}

if (buildApi || buildAll)
{
    Ando.Build(api);
}

if (buildWeb || buildAll)
{
    Ando.Build(web);
}
```

Usage:
```bash
# Build everything
ando

# Build only core
ando -p core

# Build core and api
ando -p core,api
```

## Parallel Sub-Builds

Build independent packages in parallel:

```csharp
var core = Directory("./packages/core");
var utils = Directory("./packages/utils");
var api = Directory("./packages/api");
var web = Directory("./packages/web");

// Build shared packages first (they have no dependencies)
Ando.BuildParallel(core, utils);

// Then build dependent packages in parallel
Ando.BuildParallel(api, web);

Log.Info("Parallel build complete");
```

## Share Artifacts Between Packages

Use a shared output directory:

```csharp
// Root build.csando

var artifactsDir = Root / "artifacts";

var core = Directory("./packages/core");
var api = Directory("./packages/api");

// Set shared artifact location via environment
Environment.SetEnvironmentVariable("ARTIFACTS_DIR", artifactsDir.ToString());

// Build core (outputs to artifacts)
Ando.Build(core);

// Build api (depends on core artifacts)
Ando.Build(api);
```

```csharp
// packages/core/build.csando

var project = Dotnet.Project("./src/Core/Core.csproj");
var artifactsDir = Env("ARTIFACTS_DIR");

Dotnet.Restore(project);
Dotnet.Build(project);

// Output package to shared location
Dotnet.Pack(project, o => o.Output(artifactsDir));
```

```csharp
// packages/api/build.csando

var project = Dotnet.Project("./src/Api/Api.csproj");
var artifactsDir = Env("ARTIFACTS_DIR");

// Restore from shared artifacts
Dotnet.Restore(project, o => o
    .WithSource(artifactsDir));

Dotnet.Build(project);
```

## NuGet Package Publishing

Publish multiple packages from a monorepo:

```csharp
var publish = DefineProfile("publish");

var packages = new[]
{
    Directory("./packages/core"),
    Directory("./packages/utils"),
    Directory("./packages/client"),
};

// Build all packages
foreach (var pkg in packages)
{
    Ando.Build(pkg);
}

if (publish)
{
    var version = Git.GetVersion();

    // Publish each package
    foreach (var pkg in packages)
    {
        Ando.Build(pkg, o => o
            .WithProfiles("publish")
            .WithEnvironment("VERSION", version));
    }

    // Tag release
    Git.Tag($"v{version}");
    Git.PushTags();

    Log.Info($"Published all packages at version {version}");
}
```

## Mixed Technology Monorepo

Handle .NET, Node.js, and other technologies:

```csharp
var deploy = DefineProfile("deploy");

// .NET services
var api = Directory("./services/api");
var worker = Directory("./services/worker");

// Node.js frontend
var web = Directory("./apps/web");
var admin = Directory("./apps/admin");

// Build all services
Ando.Build(api);
Ando.Build(worker);
Ando.Build(web);
Ando.Build(admin);

if (deploy)
{
    Azure.EnsureAuthenticated();
    Cloudflare.EnsureAuthenticated();

    // Deploy .NET to Azure
    Ando.Build(api, o => o.WithProfiles("deploy"));
    Ando.Build(worker, o => o.WithProfiles("deploy"));

    // Deploy frontends to Cloudflare
    Ando.Build(web, o => o.WithProfiles("deploy"));
    Ando.Build(admin, o => o.WithProfiles("deploy"));

    Log.Info("Full monorepo deployed");
}
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Ando.Build()](/providers/ando#build) | Run a sub-project's build.csando |
| [Ando.BuildParallel()](/providers/ando#buildparallel) | Build multiple projects in parallel |
| [WithProfiles()](/providers/ando#withprofiles) | Pass profiles to sub-build |
| [WithEnvironment()](/providers/ando#withenvironment) | Pass environment variables |

## Tips

- **Dependency order** - Build shared packages before dependent ones
- **Parallel when possible** - Independent packages can build in parallel
- **Share via environment** - Pass paths and config via environment variables
- **Consistent profiles** - Use the same profile names across sub-projects

## See Also

- [Ando Provider](/providers/ando) - Full API reference
- [Full Stack Deployment](/examples/fullstack-deploy) - Multi-tier deployment
- [Git Versioning](/examples/git-versioning) - Consistent versioning
