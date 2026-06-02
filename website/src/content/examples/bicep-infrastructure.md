---
title: Bicep Infrastructure
description: Deploy Azure infrastructure with Bicep templates before deploying your application.
category: Azure
tags:
  - azure
  - bicep
  - infrastructure
  - iac
---

## Overview

Bicep is Azure's domain-specific language for deploying infrastructure as code. This example shows how to deploy Azure resources with Bicep templates as part of your ANDO build pipeline, then use the deployment outputs in subsequent steps.

## Project Structure

```
my-app/
├── src/
│   └── WebApp/
│       └── WebApp.csproj
├── infra/
│   ├── main.bicep              # Main infrastructure template
│   ├── modules/
│   │   ├── appservice.bicep    # App Service module
│   │   ├── sql.bicep           # SQL Server module
│   │   └── keyvault.bicep      # Key Vault module
│   └── parameters/
│       ├── dev.bicepparam      # Development parameters
│       └── prod.bicepparam     # Production parameters
└── build.csando
```

## Basic Bicep Deployment

Deploy infrastructure and use outputs:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Deploy infrastructure
    var deployment = Bicep.DeployToResourceGroup(
        "my-app-rg",
        "./infra/main.bicep"
    );

    // Use deployment outputs
    var appServiceName = deployment.Output("appServiceName");
    var connectionString = deployment.Output("sqlConnectionString");

    Log.Info($"Deployed App Service: {appServiceName}");

    // Deploy application
    Dotnet.Publish(project, o => o.Output(Root / "publish"));
    AppService.DeployZip(appServiceName, Root / "publish");
}
```

## With Parameters File

Use environment-specific parameters:

```csharp
var deploy = DefineProfile("deploy");
var production = DefineProfile("production");

var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Select parameter file based on environment
    var paramFile = production
        ? "./infra/parameters/prod.bicepparam"
        : "./infra/parameters/dev.bicepparam";

    var resourceGroup = production ? "my-app-prod-rg" : "my-app-dev-rg";

    // Deploy with parameters
    var deployment = Bicep.DeployToResourceGroup(
        resourceGroup,
        "./infra/main.bicep",
        paramFile
    );

    // Continue with app deployment
    var appName = deployment.Output("appServiceName");
    Dotnet.Publish(project, o => o.Output(Root / "publish"));
    AppService.DeployZip(appName, Root / "publish");
}
```

## Infrastructure with Database Migration

Deploy infrastructure, run migrations, then deploy app:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Publish(project, o => o.Output(Root / "publish"));

if (deploy)
{
    Azure.EnsureAuthenticated();

    // 1. Deploy infrastructure first
    var deployment = Bicep.DeployToResourceGroup(
        "my-app-rg",
        "./infra/main.bicep"
    );

    // 2. Get connection string from deployment
    var connectionString = deployment.Output("sqlConnectionString");

    // 3. Run database migrations
    var dbContext = Ef.DbContextFrom(project, "AppDbContext");
    Ef.DatabaseUpdate(dbContext, connectionString);
    Log.Info("Database migrations applied");

    // 4. Deploy application
    var appName = deployment.Output("appServiceName");
    AppService.DeployWithSwap(appName, Root / "publish", "staging");

    Log.Info("Deployment complete!");
}
```

## What-If Preview

Preview changes before deploying:

```csharp
var deploy = DefineProfile("deploy");
var preview = DefineProfile("preview");

if (preview || deploy)
{
    Azure.EnsureAuthenticated();

    // Show what would change
    Bicep.WhatIf("my-app-rg", "./infra/main.bicep");
}

if (deploy)
{
    // Actually deploy
    var deployment = Bicep.DeployToResourceGroup(
        "my-app-rg",
        "./infra/main.bicep"
    );

    Log.Info("Infrastructure deployed");
}
```

Usage:
```bash
# Preview changes only
ando -p preview

# Deploy for real
ando -p deploy
```

## Subscription-Level Deployment

Deploy resources at subscription scope (e.g., resource groups):

```csharp
var deploy = DefineProfile("deploy");

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Deploy at subscription level (creates resource groups)
    var deployment = Bicep.DeployToSubscription(
        "eastus",
        "./infra/subscription.bicep"
    );

    var rgName = deployment.Output("resourceGroupName");
    Log.Info($"Created resource group: {rgName}");

    // Then deploy resources to the new resource group
    Bicep.DeployToResourceGroup(rgName, "./infra/resources.bicep");
}
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Bicep.DeployToResourceGroup()](/providers/bicep#deploytoresourcegroup) | Deploy to a resource group |
| [Bicep.DeployToSubscription()](/providers/bicep#deploytosubscription) | Deploy at subscription scope |
| [Bicep.WhatIf()](/providers/bicep#whatif) | Preview deployment changes |
| [deployment.Output()](/providers/bicep#output) | Get deployment output value |

## Tips

- **Use modules** - Break large templates into reusable modules
- **Parameter files** - Use `.bicepparam` files for environment-specific values
- **What-if first** - Preview changes before production deployments
- **Output secrets carefully** - Use Key Vault references instead of outputting secrets

## See Also

- [Bicep Provider](/providers/bicep) - Full API reference
- [Azure App Service](/examples/azure-app-service) - App deployment
- [EF Core Migrations](/examples/ef-migrations) - Database migrations
