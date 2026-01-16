---
layout: ../layouts/ContentLayout.astro
title: Changelog
description: Release history and changelog for ANDO.
---

## Installation

Install or update ANDO using the .NET CLI:

```bash
# Install
dotnet tool install -g ando

# Update to latest
dotnet tool update -g ando

# Check version
ando --version
```

## 0.9.3

**2026-01-16**

First public release.

**Providers:**

- **Ando** - Core operations (Log, Artifacts, Context)
- **Dotnet** - Build, test, publish .NET projects
- **DotnetSdk** - Install .NET SDK in containers
- **Ef** - Entity Framework Core migrations
- **Npm** - npm install, ci, run, build, test
- **Node** - Install Node.js in containers
- **NuGet** - Pack and push NuGet packages
- **Azure** - Azure CLI authentication and subscription management
- **Bicep** - Deploy Azure infrastructure with Bicep templates
- **AppService** - Deploy to Azure App Service with slot swapping
- **Functions** - Deploy to Azure Functions
- **Cloudflare** - Deploy to Cloudflare Pages
