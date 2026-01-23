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

## Options Reference

### Functions.DeployZip / Publish Options

| Option | Description |
|--------|-------------|
| `WithDeploymentSlot(string)` | Deploy to a specific slot instead of production. Use slots for staging, testing, or blue-green deployments. |
| `WithConfiguration(string)` | Build configuration when using `Publish`. Values: "Release" (optimized), "Debug" (with symbols). |
| `WithForceRestart()` | Force restart the function app after deployment. Ensures new code is loaded immediately without waiting for the runtime to detect changes. |
| `WithNoWait()` | Don't wait for deployment to complete. Returns immediately after starting deployment. Useful for long-running deployments in CI. |
