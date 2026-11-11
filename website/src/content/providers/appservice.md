---
title: App Service
description: Deploy and manage Azure App Service web applications.
provider: AppService
---

## AppService or Functions?

`AppService` and `Functions` have the same operations with the same signatures: `DeployZip`, `DeployWithSwap`, `SwapSlots`, `Restart`, `Start`, `Stop`, `Show`, `GetHostName`, `AccessRestrictions`, `TemporaryAccessRule`, `ListSlots`, `CreateSlot` and `DeleteSlot`. Apart from the `az` subcommand, the zip deploy differs: `AppService` always uses OneDeploy (`az webapp deploy`), while `Functions.DeployZip` defaults to `config-zip` for plan compatibility. Pick by the resource you deploy to:

|           | `AppService`                                          | `Functions`                             |
| --------- | ----------------------------------------------------- | --------------------------------------- |
| Targets   | App Service web apps (`az webapp`)                    | Azure Functions apps (`az functionapp`) |
| Only here | `DeployFolder` (zips a publish folder and deploys it) | `Publish`                               |
| Page      | this page                                             | [Functions](/providers/functions)       |

Passing a function app name to the wrong provider fails because the `az` command does not find the resource.

## Example

Deploy a web app with zero-downtime using deployment slots.

```csharp
// Build and publish the web app
var WebApp = Dotnet.Project("./src/WebApp/WebApp.csproj");
Dotnet.Publish(WebApp, o => o
  .Output(Root / "publish")
  .WithConfiguration(Configuration.Release));

// Create a reproducible zip for deployment
Archive.Zip("publish", "app.zip", o => o.WithTimestamp(Git.CommitTime));

// Deploy to staging slot, then swap to production
AppService.DeployWithSwap("my-web-app", Root / "app.zip");

// Or deploy directly to a specific slot
AppService.DeployZip("my-web-app", Root / "app.zip", "my-rg", o => o
  .WithDeploymentSlot("staging"));

// Or skip the manual zip and deploy the publish folder directly
// (contents are placed at the archive root, as zip deploy expects)
AppService.DeployFolder("my-web-app", Root / "publish", "my-rg", o => o
  .WithDeploymentSlot("staging"));
```

## Options Reference

### AppService.DeployZip Options

| Option                       | Description                                                                                                                                                                                                                                |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `WithDeploymentSlot(string)` | Deploy to a specific slot instead of production. Common slots: "staging", "dev", "canary". Slots have their own URLs and can be swapped with production.                                                                                   |
| `WithNoWait()`               | Pass `--async true` to `az webapp deploy`. Azure CLI 2.87 still polls the deployment status in some cases, so the step may not return immediately. By default the deploy is synchronous (`--async false`).                                 |
| `WithRestart()`              | Pass `--restart true` to `az webapp deploy`, so the app restarts after the deployment. Without it ANDO passes `--restart false`, because `az webapp deploy` restarts the site by default, which could start a slot you stopped on purpose. |
| `WithTimeout(TimeSpan)`      | Maximum time to wait for the zip deploy command before reconciling and retrying. Also passed to az as `--timeout`. Must be greater than zero. Default `20` minutes.                                                                        |

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

Deploys use `az webapp deploy --type zip` with `--restart false --async false` (`--restart true` with `WithRestart()`, `--async true` with `WithNoWait()`).

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
