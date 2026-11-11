---
title: Azure App Service Deployment
description: Deploy a .NET web application to Azure App Service with staging slots.
category: Azure
tags:
  - azure
  - app-service
  - dotnet
  - deployment
---

## Overview

Azure App Service provides a fully managed platform for hosting web applications. This example shows how to deploy a .NET application with zero-downtime deployments using staging slots.

## Basic Deployment

The simplest deployment deploys your published folder directly. Use `AppService.DeployZip` when you already have a zip, and `Archive.Zip` to create a reproducible one:

```csharp
var project = Dotnet.Project("./src/WebApp/WebApp.csproj");

// Build and publish
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Publish(project, o => o
    .WithConfiguration(Configuration.Release)
    .Output(Root / "publish"));

// Authenticate and deploy (DeployFolder zips the publish folder itself)
Azure.EnsureAuthenticated();
AppService.DeployFolder("my-webapp", Root / "publish");
```

## Blue-Green Deployment with Slots

For zero-downtime deployments, use staging slots:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/WebApp/WebApp.csproj");
var publishPath = Root / "publish";

// Build
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(Dotnet.Project("./tests/WebApp.Tests/WebApp.Tests.csproj"));

Dotnet.Publish(project, o => o
    .WithConfiguration(Configuration.Release)
    .Output(publishPath));

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Deploy to staging slot, then swap to production
    Archive.Zip(publishPath, Root / "publish.zip");
    AppService.DeployWithSwap("my-webapp", Root / "publish.zip", "staging");

    Log.Info("Deployment complete - swapped staging to production");
}
```

The `DeployWithSwap` operation:

1. Deploys to the staging slot
2. Warms up the staging slot
3. Swaps staging with production
4. Old production becomes the new staging (easy rollback)

## Manual Slot Control

For more control over the swap process:

```csharp
Azure.EnsureAuthenticated();

// Deploy to staging
AppService.DeployFolder("my-webapp", publishPath, configure: o => o
    .WithDeploymentSlot("staging"));

// Run smoke tests against staging
Log.Info("Running smoke tests against staging...");
// ... your smoke test logic ...

// Swap slots
AppService.SwapSlots("my-webapp", "staging");

Log.Info("Swapped staging to production");
```

## With Resource Group

Specify a resource group for apps not in your default:

```csharp
Azure.EnsureAuthenticated();
Azure.SetSubscription("my-subscription-id");

AppService.DeployFolder("my-webapp", publishPath,
    resourceGroup: "my-resource-group");
```

## Full Example with EF Migrations

Complete deployment including database migrations:

```csharp
var deploy = DefineProfile("deploy");

var webProject = Dotnet.Project("./src/WebApp/WebApp.csproj");
var testProject = Dotnet.Project("./tests/WebApp.Tests/WebApp.Tests.csproj");
var publishPath = Root / "publish";

// Build and test
Dotnet.SdkInstall();
Dotnet.Restore(webProject);
Dotnet.Build(webProject);
Dotnet.Test(testProject);

// Publish
Dotnet.Publish(webProject, o => o
    .WithConfiguration(Configuration.Release)
    .Output(publishPath));

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Run EF migrations first
    var dbContext = Ef.DbContextFrom(webProject, "AppDbContext");
    Ef.DatabaseUpdate(dbContext, Env("CONNECTION_STRING"));

    // Deploy with slot swap
    Archive.Zip(publishPath, Root / "publish.zip");
    AppService.DeployWithSwap("my-webapp", Root / "publish.zip", "staging");

    Log.Info("Deployment complete!");
}
```

## Key Operations

| Operation                                                           | Purpose                   |
| ------------------------------------------------------------------- | ------------------------- |
| [Azure.EnsureAuthenticated()](/providers/azure#ensureauthenticated) | Authenticate with Azure   |
| [AppService.DeployZip()](/providers/appservice#deployzip)           | Deploy via zip deployment |
| [AppService.DeployWithSwap()](/providers/appservice#deploywithswap) | Deploy to slot and swap   |
| [AppService.SwapSlots()](/providers/appservice#swapslots)           | Swap deployment slots     |

## See Also

- [Azure Provider](/providers/azure) - Azure authentication
- [App Service Provider](/providers/appservice) - Full API reference
- [EF Core Migrations](/examples/ef-migrations) - Database migrations
