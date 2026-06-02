---
title: Azure Functions Deployment
description: Build and deploy serverless Azure Functions with zip deployment.
category: Azure
tags:
  - azure
  - functions
  - serverless
  - dotnet
---

## Overview

Azure Functions provides serverless compute for event-driven applications. This example shows how to build and deploy .NET Azure Functions using ANDO.

## Basic Deployment

Deploy a Functions project using zip deployment:

```csharp
var project = Dotnet.Project("./src/MyFunctions/MyFunctions.csproj");
var publishPath = Root / "publish";

// Build and publish
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Publish(project, o => o
    .WithConfiguration(Configuration.Release)
    .Output(publishPath));

// Authenticate and deploy
Azure.EnsureAuthenticated();
Functions.DeployZip("my-function-app", publishPath);
```

## Deployment with Staging Slot

Use slots for zero-downtime deployments:

```csharp
var deploy = DefineProfile("deploy");

var project = Dotnet.Project("./src/MyFunctions/MyFunctions.csproj");
var testProject = Dotnet.Project("./tests/MyFunctions.Tests/MyFunctions.Tests.csproj");
var publishPath = Root / "publish";

// Build and test
Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);
Dotnet.Test(testProject);

Dotnet.Publish(project, o => o
    .WithConfiguration(Configuration.Release)
    .Output(publishPath));

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Deploy to staging, then swap
    Functions.DeployWithSwap("my-function-app", publishPath, "staging");

    Log.Info("Functions deployed and swapped to production");
}
```

## Using func CLI Publish

For projects using the Azure Functions Core Tools:

```csharp
var deploy = DefineProfile("deploy");

var projectPath = Directory("./src/MyFunctions");

// Build
Dotnet.Restore(Dotnet.Project(projectPath / "MyFunctions.csproj"));
Dotnet.Build(Dotnet.Project(projectPath / "MyFunctions.csproj"));

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Uses 'func azure functionapp publish' under the hood
    Functions.Publish("my-function-app", projectPath);
}
```

## With Resource Group and Subscription

Specify resource group for non-default apps:

```csharp
Azure.EnsureAuthenticated();
Azure.SetSubscription("my-subscription-id");

Functions.DeployZip("my-function-app", publishPath,
    resourceGroup: "my-resource-group");
```

## Isolated Worker Model

For .NET isolated worker functions (recommended for .NET 8+):

```csharp
var project = Dotnet.Project("./src/MyFunctions/MyFunctions.csproj");
var publishPath = Root / "publish";

Dotnet.Restore(project);
Dotnet.Build(project);

// Isolated workers publish as self-contained by default
Dotnet.Publish(project, o => o
    .WithConfiguration(Configuration.Release)
    .Output(publishPath));

Azure.EnsureAuthenticated();
Functions.DeployZip("my-function-app", publishPath);
```

## Full Example with Multiple Function Apps

Deploy multiple function apps from a monorepo:

```csharp
var deploy = DefineProfile("deploy");

var httpFunctions = Dotnet.Project("./src/HttpFunctions/HttpFunctions.csproj");
var timerFunctions = Dotnet.Project("./src/TimerFunctions/TimerFunctions.csproj");

// Build all
Dotnet.SdkInstall();
Dotnet.Restore(httpFunctions);
Dotnet.Restore(timerFunctions);
Dotnet.Build(httpFunctions);
Dotnet.Build(timerFunctions);

// Publish
Dotnet.Publish(httpFunctions, o => o
    .WithConfiguration(Configuration.Release)
    .Output(Root / "publish" / "http"));

Dotnet.Publish(timerFunctions, o => o
    .WithConfiguration(Configuration.Release)
    .Output(Root / "publish" / "timer"));

if (deploy)
{
    Azure.EnsureAuthenticated();

    // Deploy both function apps
    Functions.DeployZip("my-http-functions", Root / "publish" / "http");
    Functions.DeployZip("my-timer-functions", Root / "publish" / "timer");

    Log.Info("All function apps deployed");
}
```

## Key Operations

| Operation | Purpose |
|-----------|---------|
| [Azure.EnsureAuthenticated()](/providers/azure#ensureauthenticated) | Authenticate with Azure |
| [Functions.DeployZip()](/providers/functions#deployzip) | Deploy via zip deployment |
| [Functions.DeployWithSwap()](/providers/functions#deploywithswap) | Deploy to slot and swap |
| [Functions.Publish()](/providers/functions#publish) | Deploy using func CLI |

## See Also

- [Azure Provider](/providers/azure) - Azure authentication
- [Functions Provider](/providers/functions) - Full API reference
- [Azure App Service](/examples/azure-app-service) - Web app deployment
