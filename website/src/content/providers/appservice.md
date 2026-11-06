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

| Option                       | Description                                                                                                                                                                                        |
| ---------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WithDeploymentSlot(string)` | Deploy to a specific slot instead of production. Common slots: "staging", "dev", "canary". Slots have their own URLs and can be swapped with production.                                           |
| `WithNoWait()`               | Don't wait for deployment to complete. Returns immediately after starting the deployment. Use when you don't need to verify deployment success in the build. By default the deploy is synchronous. |
| `WithRestart()`              | After a successful deployment, run `az webapp restart` for the same app, resource group and slot, so the app picks up new code immediately. With `WithNoWait()` the restart does not wait for the deployment. By default the app is not restarted.                           |
| `WithTimeout(TimeSpan)`      | Maximum time to wait for the zip deploy command before reconciling and retrying. Must be greater than zero. Default `20` minutes.                                                                  |

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

Deploys use `az webapp deployment source config-zip`.

`DeployFolder` zips the folder itself and installs `zip` with apt in the container if it is missing (Debian and Ubuntu images). The image `mcr.microsoft.com/dotnet/sdk:10.0` (Ubuntu 24.04) does not include `zip`. For reproducible builds, zip at build time (so the same bytes are deployed every time) and use `DeployZip`.

## Reading app state

These steps return a `CommandStep`. Read the result at execution time, for example in `Ando.When`, `Ando.Require` or a `Func<>` overload. See the Ando provider page for the step model.

| Operation                                        | Description                                                                                     |
| ------------------------------------------------ | ----------------------------------------------------------------------------------------------- |
| `AppService.Show(app, rg?, slot?)`               | The app's (or slot's) `az webapp show` JSON. Quiet. Read fields with `Json("state")`.           |
| `AppService.GetHostName(app, rg?, slot?)`        | The default host name, for example `myapp-release.azurewebsites.net`. `Value` is the host name. |
| `AppService.AccessRestrictions(app, rg?, slot?)` | The `az webapp config access-restriction show` JSON. Quiet.                                     |

```csharp
var host = AppService.GetHostName("my-web-app", "my-rg", "release");
Http.WaitForHealthy(() => $"https://{host.Value}/healthz");

var show = AppService.Show("my-web-app", "my-rg", "release");
Ando.Require(() => show.Json("state") == "Running", "The release slot is not running");
```

## Temporary access rule

`AppService.TemporaryAccessRule(app, rg, slot = null, ruleName = "ando-deploy", ip = null, priority = 50)` adds an Allow access rule for one IPv4 address to the app or slot, so the build can reach a restricted site. It removes the rule again when the enclosing `Ando.Block` (or the run) ends, whatever happened: the removal is a finally step, followed by a check that the rule is gone. An existing rule with the same name is replaced.

Without `ip`, ANDO uses this machine's public IPv4 address. A bare address gets `/32`; a CIDR range is used as given.

It can also be called inside `Ando.When` or `Ando.Defer` (for example only when the slot denies traffic by default); the rule is still removed when the enclosing block ends.

```csharp
Ando.Block("deploy", () =>
{
    AppService.TemporaryAccessRule("my-web-app", "my-rg", "release", "deploy-gate");
    AppService.DeployFolder("my-web-app", Root / "publish", "my-rg", o => o
      .WithDeploymentSlot("release"));
    Http.WaitForHealthy(() => $"https://{host.Value}/healthz");
});
```
