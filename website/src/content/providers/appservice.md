---
title: App Service
description: Deploy and manage Azure App Service web applications.
provider: AppService
---

## Example

Deploy a web app with zero-downtime using deployment slots.

```csharp
// Build and publish the web app
var WebApp = Dotnet.Project("./src/WebApp/WebApp.csproj");
Dotnet.Publish(WebApp, o => o
  .Output(Root / "publish")
  .WithConfiguration(Configuration.Release));

// Create zip for deployment
Artifact.Zip(Root / "publish", Root / "app.zip");

// Deploy to staging slot, then swap to production
AppService.DeployWithSwap("my-web-app", Root / "app.zip");

// Or deploy directly to a specific slot
AppService.DeployZip("my-web-app", Root / "app.zip", "my-rg", o => o
  .WithDeploymentSlot("staging"));

// Or skip the manual zip and deploy the publish folder directly
// (contents are placed at the archive root, as config-zip expects)
AppService.DeployFolder("my-web-app", Root / "publish", "my-rg", o => o
  .WithDeploymentSlot("staging"));
```

## Options Reference

### AppService.DeployZip Options

| Option                       | Description                                                                                                                                                  |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `WithDeploymentSlot(string)` | Deploy to a specific slot instead of production. Common slots: "staging", "dev", "canary". Slots have their own URLs and can be swapped with production.     |
| `WithNoWait()`               | Don't wait for deployment to complete. Returns immediately after starting the deployment. Use when you don't need to verify deployment success in the build. |
| `WithRestart()`              | Restart the app after deployment. Forces the app to pick up new code immediately instead of waiting for the next scheduled restart.                          |
| `WithTimeout(TimeSpan)`      | Maximum time to wait for the zip deploy command before reconciling and retrying. Must be greater than zero. Default `20` minutes.                            |

## Resilient zip deploy

`AppService.DeployZip` (and the operations built on it) tolerate the flaky
behaviour Azure App Service sometimes exhibits during large uploads:

- **Timeout reconciliation.** If the `az webapp deploy` command times out, ANDO
  queries the latest Kudu deployment status. When Kudu reports the deployment
  completed successfully after the command timed out, the step is treated as
  successful instead of failing.
- **Automatic retries.** Transient failures (HTTP 5xx, "Service Unavailable",
  connection resets) are retried with backoff, up to three attempts, as long as
  the overall `WithTimeout` budget has not been exhausted.
