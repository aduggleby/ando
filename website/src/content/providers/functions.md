---
title: Azure Functions
description: Deploy and manage Azure Functions apps.
provider: Functions
---

## Functions or AppService?

`Functions` and `AppService` have the same operations with the same signatures: `DeployZip`, `DeployWithSwap`, `SwapSlots`, `Restart`, `Start`, `Stop`, `Show`, `GetHostName`, `AccessRestrictions`, `TemporaryAccessRule`, `ListSlots`, `CreateSlot` and `DeleteSlot`. Apart from the `az` subcommand, the zip deploy differs: `AppService` always uses OneDeploy (`az webapp deploy`), while `Functions.DeployZip` defaults to `config-zip` for plan compatibility and uses OneDeploy only with `WithOneDeploy()`. Pick by the resource you deploy to:

|           | `Functions`                                                              | `AppService`                        |
| --------- | ------------------------------------------------------------------------ | ----------------------------------- |
| Targets   | Azure Functions apps (`az functionapp`)                                  | App Service web apps (`az webapp`)  |
| Only here | `Publish` (Azure Functions Core Tools, `func azure functionapp publish`) | `DeployFolder`                      |
| Page      | this page                                                                | [AppService](/providers/appservice) |

Passing a web app name to the wrong provider fails because the `az` command does not find the resource.

## Example

Deploy a function app with zero-downtime using slot swap.

```csharp
// Build and publish the function app
var FuncApp = Dotnet.Project("./src/MyFunc/MyFunc.csproj");
Dotnet.Publish(FuncApp, o => o
  .Output(Root / "publish")
  .WithConfiguration(Configuration.Release));

// Create a reproducible zip for deployment
Archive.Zip("publish", "func.zip", o => o.WithTimestamp(Git.CommitTime));

// Deploy with zero-downtime swap
Functions.DeployWithSwap("my-func-app", Root / "func.zip");
```

## Options Reference

### Functions.DeployZip Options

| Option                       | Description                                                                                                                                                                                                               |
| ---------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WithDeploymentSlot(string)` | Deploy to a specific slot instead of production (`--slot`).                                                                                                                                                               |
| `WithOneDeploy()`            | Use OneDeploy (`az functionapp deploy --type zip`, preview) instead of the default `config-zip`. See [Zip deploy](#zip-deploy).                                                                                           |
| `WithNoWait()`               | **OneDeploy only.** Pass `--async true`. By default the deployment is awaited (`--async false`).                                                                                                                          |
| `WithRestart()`              | **OneDeploy only.** Pass `--restart true`. Without it ANDO passes `--restart false`, because OneDeploy restarts the app by default, which could start a slot you stopped on purpose.                                      |
| `WithTimeout(TimeSpan)`      | Maximum time to wait for the deploy command before retrying; also passed to az as `--timeout` (seconds for `config-zip`, milliseconds for OneDeploy). Works on both paths. Must be greater than zero. Default 20 minutes. |

### Functions.Publish Options

| Option                       | Description                                      |
| ---------------------------- | ------------------------------------------------ |
| `WithDeploymentSlot(string)` | Publish to a specific slot.                      |
| `WithConfiguration(string)`  | Build configuration. Values: "Release", "Debug". |
| `WithForceRestart()`         | Force restart the function app after publishing. |
| `WithNoWait()`               | Don't wait for the deployment to complete.       |

## Zip deploy

`Functions.DeployZip` has two mechanisms.

**Default: `config-zip`.** Runs `az functionapp deployment source config-zip --name N [--resource-group RG] --src ZIP [--slot S] [--timeout <seconds>] --output none`. It supports every plan: dedicated and Premium, Linux Consumption (the zip is uploaded to storage and `WEBSITE_RUN_FROM_PACKAGE` is set) and Flex Consumption. `DeployWithSwap` uses this path.

**Opt-in: `WithOneDeploy()`.** Runs `az functionapp deploy --src-path ZIP --type zip [--slot S] --restart false|true --async false|true [--timeout <ms>]`. This command is in preview and posts to `/api/publish`. It fits dedicated, Premium and Flex plans and is not guaranteed on Linux Consumption. `AppService.DeployZip` always uses OneDeploy (`az webapp deploy`).

- `WithRestart()` and `WithNoWait()` need `WithOneDeploy()`: without it `DeployZip` throws, because `config-zip` has no restart or async option. (**Behavior change:** these two options used to apply without it.)
- `WithTimeout` and `WithDeploymentSlot` work on both paths.
- Transient failures (HTTP 5xx, connection resets) are retried up to 3 times on both paths.
- After a timeout, only OneDeploy checks `az functionapp log deployment show` and treats a deployment that completed anyway as successful. A timed-out `config-zip` fails.

```csharp
// Default (all plans)
Functions.DeployZip(app, "out/func.zip", rg, o => o
    .WithDeploymentSlot("release")
    .WithTimeout(TimeSpan.FromMinutes(30)));

// OneDeploy (preview): restart after the deploy
Functions.DeployZip(app, "out/func.zip", rg, o => o
    .WithOneDeploy()
    .WithRestart());
```

## Reading app state and gating deploys

`SwapSlots`, `Restart`, `Stop`, `Start`, `Show`, `GetHostName` and `AccessRestrictions` return a `CommandStep`; read the result at execution time (see the Ando provider page, "Step model"). `SwapSlots` has a 30-minute default timeout.

| Operation                                                                                                         | Description                                                                                                                                                                       |
| ----------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Functions.Show(app, rg?, slot?)`                                                                                 | The app's (or slot's) `az functionapp show` JSON. Quiet. Read fields with `Json("state")`.                                                                                        |
| `Functions.GetHostName(app, rg?, slot?)`                                                                          | The default host name (`az functionapp show --query defaultHostName -o tsv`). `Value` is the host name.                                                                           |
| `Functions.AccessRestrictions(app, rg?, slot?)`                                                                   | `az functionapp config access-restriction show` JSON. Quiet.                                                                                                                      |
| `Functions.TemporaryAccessRule(app, rg, slot?, ruleName = "ando-deploy", ip?, priority = 50)`                     | Adds an Allow rule for one IPv4 address (default: this machine's public IP) and removes it in a finally step (`Functions.RemoveAccessRule`) when the enclosing block or run ends. |
| `Functions.ListSlots(app, rg?)`, `CreateSlot(app, slot, rg?, configurationSource?)`, `DeleteSlot(app, slot, rg?)` | Manage deployment slots.                                                                                                                                                          |

```csharp
var h = Functions.GetHostName(app, rg, "release");
Ando.Block("deploy", () =>
{
    Functions.TemporaryAccessRule(app, rg, "release", "deploy-gate");
    Functions.DeployZip(app, "out/func.zip", rg, o => o.WithDeploymentSlot("release"));
    Http.WaitForHealthy(() => $"https://{h.Value}/api/health");
    Functions.SwapSlots(app, "release", rg);
});
```
