---
title: Cloudflare Workers
description: Deploy serverless functions to Cloudflare Workers at the edge.
category: Frontend
tags:
  - cloudflare
  - workers
  - serverless
  - edge
---

## Overview

Cloudflare Workers run JavaScript at the edge, close to your users. This example shows how to build and deploy Workers as part of your ANDO build pipeline.

## Project Structure

```
my-worker/
├── src/
│   └── index.ts                 # Worker entry point
├── wrangler.toml                # Wrangler configuration
├── package.json
└── build.csando
```

## Basic Worker Deployment

Build and deploy a Cloudflare Worker:

```csharp
var deploy = DefineProfile("deploy");

var worker = Directory("./");

// Install dependencies
Node.Install();
Npm.Ci(worker);

// Build worker (if using TypeScript or bundler)
Npm.Build(worker);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Deploy worker
    Cloudflare.WorkersDeploy(worker, "my-worker");

    Log.Info("Worker deployed to Cloudflare");
}
```

## Worker with KV Namespace

Deploy worker with KV storage:

```csharp
var deploy = DefineProfile("deploy");

var worker = Directory("./");

Node.Install();
Npm.Ci(worker);
Npm.Build(worker);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Ensure KV namespace exists
    var kvNamespace = Cloudflare.KvNamespaceCreate("MY_KV", o => o
        .WithIfNotExists());

    // Deploy worker with KV binding
    Cloudflare.WorkersDeploy(worker, "my-worker", o => o
        .WithKvBinding("MY_KV", kvNamespace));

    Log.Info("Worker deployed with KV namespace");
}
```

## Worker with D1 Database

Deploy worker with D1 SQLite database:

```csharp
var deploy = DefineProfile("deploy");
var migrate = DefineProfile("migrate");

var worker = Directory("./");

Node.Install();
Npm.Ci(worker);
Npm.Build(worker);

if (migrate || deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Ensure D1 database exists
    var database = Cloudflare.D1Create("my-db", o => o.WithIfNotExists());

    // Run migrations
    Cloudflare.D1Execute(database, File("./migrations/001_init.sql"));

    Log.Info("Database migrations applied");
}

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    var database = Cloudflare.D1Get("my-db");

    // Deploy worker with D1 binding
    Cloudflare.WorkersDeploy(worker, "my-worker", o => o
        .WithD1Binding("DB", database));

    Log.Info("Worker deployed with D1 database");
}
```

## Multi-Environment Deployment

Deploy to staging and production environments:

```csharp
var deploy = DefineProfile("deploy");
var production = DefineProfile("production");

var worker = Directory("./");

Node.Install();
Npm.Ci(worker);
Npm.Build(worker);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    var workerName = production ? "my-worker" : "my-worker-staging";
    var route = production
        ? "api.example.com/*"
        : "api-staging.example.com/*";

    // Deploy worker
    Cloudflare.WorkersDeploy(worker, workerName, o => o
        .WithRoute(route));

    Log.Info($"Deployed to {workerName}");
}
```

Usage:
```bash
# Deploy to staging
ando -p deploy

# Deploy to production
ando -p deploy,production
```

## API Backend with Worker

Full-stack: frontend on Pages, API on Workers:

```csharp
var deploy = DefineProfile("deploy");

var frontend = Directory("./frontend");
var api = Directory("./api");

// Build frontend
Node.Install();
Npm.Ci(frontend);
Npm.Build(frontend);

// Build API worker
Npm.Ci(api);
Npm.Build(api);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Deploy API worker
    Cloudflare.WorkersDeploy(api, "my-api", o => o
        .WithRoute("api.example.com/*"));

    // Deploy frontend to Pages
    Cloudflare.PagesDeploy(frontend / "dist", "my-frontend");

    // Purge cache
    Cloudflare.PurgeCache("example.com");

    Log.Info("Full stack deployed");
}
```

## Scheduled Worker (Cron)

Deploy worker with cron triggers:

```csharp
var deploy = DefineProfile("deploy");

var worker = Directory("./");

Node.Install();
Npm.Ci(worker);
Npm.Build(worker);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Deploy worker with cron schedule
    Cloudflare.WorkersDeploy(worker, "my-cron-worker", o => o
        .WithCron("0 * * * *")      // Every hour
        .WithCron("0 0 * * *"));    // Daily at midnight

    Log.Info("Cron worker deployed");
}
```

## Worker with Secrets

Deploy with environment secrets:

```csharp
var deploy = DefineProfile("deploy");

var worker = Directory("./");

Node.Install();
Npm.Ci(worker);
Npm.Build(worker);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Set secrets (not stored in wrangler.toml)
    Cloudflare.WorkersSecretPut("my-worker", "API_KEY", Env("API_KEY"));
    Cloudflare.WorkersSecretPut("my-worker", "DATABASE_URL", Env("DATABASE_URL"));

    // Deploy worker
    Cloudflare.WorkersDeploy(worker, "my-worker");

    Log.Info("Worker deployed with secrets");
}
```

## Example wrangler.toml

```toml
name = "my-worker"
main = "src/index.ts"
compatibility_date = "2024-01-01"

[vars]
ENVIRONMENT = "production"

[[kv_namespaces]]
binding = "MY_KV"
id = "abcd1234..."

[[d1_databases]]
binding = "DB"
database_name = "my-db"
database_id = "efgh5678..."

[triggers]
crons = ["0 * * * *"]

[[routes]]
pattern = "api.example.com/*"
zone_name = "example.com"
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Cloudflare.WorkersDeploy()](/providers/cloudflare#workersdeploy) | Deploy a Worker |
| [Cloudflare.KvNamespaceCreate()](/providers/cloudflare#kvnamespacecreate) | Create KV namespace |
| [Cloudflare.D1Create()](/providers/cloudflare#d1create) | Create D1 database |
| [Cloudflare.D1Execute()](/providers/cloudflare#d1execute) | Run SQL on D1 |
| [Cloudflare.WorkersSecretPut()](/providers/cloudflare#workerssecretput) | Set worker secret |

## Tips

- **Use wrangler.toml** - Define bindings and routes in config
- **Secrets via ANDO** - Don't commit secrets, set them during deployment
- **Test locally** - Use `wrangler dev` for local testing
- **Staged rollouts** - Deploy to staging first, then production

## See Also

- [Cloudflare Provider](/providers/cloudflare) - Full API reference
- [Astro + Cloudflare](/examples/astro) - Static site deployment
- [Full Stack Deployment](/examples/fullstack-deploy) - Frontend + backend
