---
title: Azure Functions
description: Deploy and manage Azure Functions apps.
provider: Functions
---

## Example

Deploy a function app with zero-downtime using slot swap.

```csharp
// Build and publish the function app
var FuncApp = Dotnet.Project("./src/MyFunc/MyFunc.csproj");
Dotnet.Publish(FuncApp, o => o
  .Output(Root / "publish")
  .WithConfiguration(Configuration.Release));

// Create zip for deployment
Artifact.Zip(Root / "publish", Root / "func.zip");

// Deploy with zero-downtime swap
Functions.DeployWithSwap("my-func-app", Root / "func.zip");
```
