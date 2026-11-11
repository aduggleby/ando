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

## How Workers are deployed

ANDO has Cloudflare operations for Pages (`PagesDeploy` and friends) and `PurgeCache`, but none for Workers. A Worker is deployed with Wrangler, which you run through an npm script with `Npm.Run`. Bindings (KV, D1), routes and cron triggers are configured in `wrangler.toml`, not in the build script. Wrangler reads `CLOUDFLARE_API_TOKEN` and `CLOUDFLARE_ACCOUNT_ID` from the environment (for example from `.env.ando`).

Add the scripts to `package.json`:

```json
{
  "scripts": {
    "build": "tsc",
    "deploy": "wrangler deploy",
    "deploy:staging": "wrangler deploy --env staging",
    "db:migrate": "wrangler d1 migrations apply my-db --remote"
  },
  "devDependencies": {
    "wrangler": "^3.0.0"
  }
}
```

## Basic Worker Deployment

Build and deploy a Cloudflare Worker:

```csharp
var deploy = DefineProfile("deploy");

var worker = Directory("./");

// Install dependencies
Node.Install();
Npm.Ci(worker);

// Build worker (if using TypeScript or a bundler)
Npm.Build(worker);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Deploy worker with Wrangler
    Npm.Run(worker, "deploy");

    Log.Info("Worker deployed to Cloudflare");
}
```

## Worker with KV Namespace or D1 Database

Declare the bindings in `wrangler.toml` (see the example below). `wrangler deploy` creates the binding on the Worker; create the KV namespace or D1 database once with `wrangler kv namespace create` or `wrangler d1 create`.

Apply D1 migrations before deploying:

```csharp
var deploy = DefineProfile("deploy");

var worker = Directory("./");

Node.Install();
Npm.Ci(worker);
Npm.Build(worker);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();

    // Apply D1 migrations from ./migrations, then deploy
    Npm.Run(worker, "db:migrate");
    Npm.Run(worker, "deploy");

    Log.Info("Worker deployed with D1 migrations applied");
}
```

## Multi-Environment Deployment

Define `[env.staging]` in `wrangler.toml` and pick the script by profile:

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

    Npm.Run(worker, production ? "deploy" : "deploy:staging");

    Log.Info(production ? "Deployed to production" : "Deployed to staging");
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

    // Deploy API worker (routes are in api/wrangler.toml)
    Npm.Run(api, "deploy");

    // Deploy frontend to Pages
    Cloudflare.PagesDeploy(frontend / "dist", "my-frontend");

    // Purge cache
    Cloudflare.PurgeCache("example.com");

    Log.Info("Full stack deployed");
}
```

## Scheduled Worker (Cron)

Cron triggers are part of `wrangler.toml` (`[triggers] crons = [...]`), so deploying the Worker updates the schedule:

```csharp
var deploy = DefineProfile("deploy");

var worker = Directory("./");

Node.Install();
Npm.Ci(worker);
Npm.Build(worker);

if (deploy)
{
    Cloudflare.EnsureAuthenticated();
    Npm.Run(worker, "deploy");

    Log.Info("Cron worker deployed");
}
```

## Worker Secrets

Secrets are not stored in `wrangler.toml` and cannot be set by an ANDO operation. Set them once with `wrangler secret put <NAME>` (or `wrangler secret bulk`), or use `[vars]` for non-secret values. The deployed Worker keeps its secrets across deployments.

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

[env.staging]
name = "my-worker-staging"
```

## Key Operations

| Operation                                                                     | Purpose                                        |
| ----------------------------------------------------------------------------- | ---------------------------------------------- |
| [Npm.Run()](/providers/npm#run)                                               | Run the `wrangler` scripts from `package.json` |
| [Cloudflare.EnsureAuthenticated()](/providers/cloudflare#ensureauthenticated) | Verify the Cloudflare API token                |
| [Cloudflare.PagesDeploy()](/providers/cloudflare#pagesdeploy)                 | Deploy a frontend to Pages                     |
| [Cloudflare.PurgeCache()](/providers/cloudflare#purgecache)                   | Purge the zone cache                           |

## Tips

- **Use wrangler.toml** - Define bindings, routes and cron triggers in config
- **Secrets stay out of the repo** - Set them with `wrangler secret put`, never in code
- **Test locally** - Use `wrangler dev` for local testing
- **Staged rollouts** - Deploy to staging first, then production

## See Also

- [Cloudflare Provider](/providers/cloudflare) - Full API reference
- [Astro + Cloudflare](/examples/astro) - Static site deployment
- [Full Stack Deployment](/examples/fullstack-deploy) - Frontend + backend
