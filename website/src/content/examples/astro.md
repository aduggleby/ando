---
title: Astro + Cloudflare Pages
description: Build an Astro site and deploy it to Cloudflare Pages with cache purging.
category: Frontend
tags:
  - astro
  - cloudflare
  - static-site
---

## Overview

This example demonstrates a complete workflow for building and deploying a static Astro website to Cloudflare Pages. The build runs in an Ubuntu container with Node.js installed automatically.

The workflow performs these steps:

1. Installs Node.js in the container
2. Installs npm dependencies
3. Builds the Astro site
4. Authenticates with Cloudflare
5. Deploys to Cloudflare Pages
6. Purges the Cloudflare cache so visitors see the latest content

## Build Script

```csharp
// Create a directory reference for the website project.
var website = Directory(".");

// Install Node.js (includes npm) in the Ubuntu container.
Node.Install();

// Install dependencies.
Npm.Install(website);

// Build the Astro site (outputs to ./dist).
Npm.Build(website);

// Verify Cloudflare authentication.
Cloudflare.EnsureAuthenticated();

// Deploy to Cloudflare Pages.
Cloudflare.PagesDeploy(website / "dist", "my-site");

// Purge the Cloudflare cache to ensure visitors see the latest content.
Cloudflare.PurgeCache("example.com");
```

## Prerequisites

- `CLOUDFLARE_API_TOKEN` environment variable (or enter when prompted)
- `CLOUDFLARE_ACCOUNT_ID` environment variable (or enter when prompted)
- A Cloudflare Pages project already created

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Node.Install()](/providers/node) | Installs Node.js v22 in the container |
| [Npm.Install()](/providers/npm#install) | Runs `npm install` to install dependencies |
| [Npm.Build()](/providers/npm#build) | Runs `npm run build` to build the Astro site |
| [Cloudflare.EnsureAuthenticated()](/providers/cloudflare#ensureauthenticated) | Verifies Cloudflare credentials are available |
| [Cloudflare.PagesDeploy()](/providers/cloudflare#pagesdeploy) | Deploys the `dist` folder to Cloudflare Pages |
| [Cloudflare.PurgeCache()](/providers/cloudflare#purgecache) | Clears the CDN cache so visitors see the latest content |

## Running the Build

```bash
ando
```

The build runs inside a Docker container. On first run, ANDO will prompt for Cloudflare credentials if the environment variables aren't set.
