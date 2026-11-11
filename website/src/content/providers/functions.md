---
title: Azure Functions
description: Deploy and manage Azure Functions apps.
provider: Functions
---

## Functions or AppService?

`Functions` and `AppService` have the same operations with the same signatures and behavior: `DeployZip`, `DeployWithSwap`, `SwapSlots`, `Restart`, `Start`, `Stop`, `Show`, `GetHostName`, `AccessRestrictions`, `TemporaryAccessRule`, `ListSlots`, `CreateSlot` and `DeleteSlot`. Only the `az` subcommand differs, so pick by the resource you deploy to:

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

// Create zip for deployment
Artifact.Zip(Root / "publish", Root / "func.zip");

// Deploy with zero-downtime swap
Functions.DeployWithSwap("my-func-app", Root / "func.zip");
```

## Options Reference

### Functions.DeployZip Options

| Option                       | Description                                                                                                                                                      |
| ---------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WithDeploymentSlot(string)` | Deploy to a specific slot instead of production (`--slot`).                                                                                                      |
| `WithNoWait()`               | Pass `--async true`. By default the deployment is awaited (`--async false`).                                                                                     |
| `WithRestart()`              | Pass `--restart true`. Without it ANDO passes `--restart false`, because OneDeploy restarts the app by default, which could start a slot you stopped on purpose. |
| `WithTimeout(TimeSpan)`      | Maximum time to wait for the deploy command before reconciling and retrying; also passed to az as `--timeout`. Must be greater than zero. Default 20 minutes.    |

### Functions.Publish Options

| Option                       | Description                                      |
| ---------------------------- | ------------------------------------------------ |
| `WithDeploymentSlot(string)` | Publish to a specific slot.                      |
| `WithConfiguration(string)`  | Build configuration. Values: "Release", "Debug". |
| `WithForceRestart()`         | Force restart the function app after publishing. |
| `WithNoWait()`               | Don't wait for the deployment to complete.       |

## Zip deploy

`Functions.DeployZip` runs `az functionapp deploy --name N [--resource-group RG] --src-path ZIP --type zip [--slot S] --restart false|true --async false|true [--timeout ms] --output none`. It behaves like `AppService.DeployZip`: transient failures (HTTP 5xx, connection resets) are retried up to 3 times within the timeout budget, and after a timeout ANDO checks `az functionapp log deployment show` and treats a deployment that completed anyway as successful. Earlier versions used `config-zip`.

```csharp
Functions.DeployZip(app, "out/func.zip", rg, o => o
    .WithDeploymentSlot("release")
    .WithRestart()
    .WithTimeout(TimeSpan.FromMinutes(30)));
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
