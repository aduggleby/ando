---
title: Bicep
description: Deploy Azure infrastructure using Bicep templates.
provider: Bicep
---

## Example

Deploy infrastructure and use outputs in subsequent steps.

```csharp
// Validate, then preview the changes and gate the deployment on the plan
Bicep.Validate("my-app-rg", "./infra/main.bicep", o => o
  .WithParameterFile("./infra/params.prod.json"));
var plan = Bicep.WhatIf("my-app-rg", "./infra/main.bicep", o => o
  .WithParameterFile("./infra/params.prod.json"));
Ando.Require(() => !plan.HasChanges("Delete"), "The plan deletes resources.");
Ando.Confirm(() => $"Deploy ({plan.Summary})?");

// Deploy - returns BicepDeployment with typed output access
var deployment = Bicep.DeployToResourceGroup("my-app-rg", "./infra/main.bicep", o => o
  .WithParameterFile("./infra/params.prod.json"));

// Pass deployment outputs to other operations
Ef.DatabaseUpdate(DbContext, deployment.Output("sqlConnectionString"));

// Build Bicep to ARM for validation
Bicep.Build("./infra/main.bicep", "./artifacts/main.json");
```

## Options Reference

### Options (deploy, validate and what-if, both scopes)

| Option                             | Description                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                                   |
| ---------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `WithName(string)`                 | Custom deployment name. Defaults to the template filename. Useful for tracking deployments in Azure Portal activity log.                                                                                                                                                                                                                                                                                                                                                                                                                      |
| `WithParameterFile(string)`        | Adds a parameters file. Call it repeatedly for .json files: each file becomes `--parameters @file`, in call order, and az lets later files override earlier values. A `.bicepparam` file must be the only parameter file: az allows only inline `KEY=VALUE` parameters next to it, so combining it with another file or with `WithSecureParameter` throws when the step is registered.                                                                                                                                                        |
| `WithParameter(name, value)`       | Adds an inline parameter value. It goes on the az command line, so it shows in process listings: use it for non-secret values only. Can be called multiple times.                                                                                                                                                                                                                                                                                                                                                                             |
| `WithSecureParameter(name, value)` | Adds a secret parameter. The value is registered for redaction at the call. When the step runs, ANDO writes all secure parameters to one temporary parameters file in the build container (`/tmp/ando-bicep-<guid>.json`, mode 0600), passes it as the last `--parameters @file`, and deletes it afterwards, also when az fails. The value never appears on a command line or in logs. Values shorter than 6 characters are not redacted from logs. The JSON-escaped form of the value is redacted too. Not usable with a `.bicepparam` file. |
| `WithMode(DeploymentMode)`         | Deployment mode: `DeploymentMode.Incremental` (default) adds/updates resources but doesn't delete missing ones. `DeploymentMode.Complete` deletes resources not in the template (use with caution). `Complete` works at resource group scope only: az has no `--mode` at subscription scope, so `DeployToSubscription`, `ValidateSubscription` and `WhatIfSubscription` throw when the step is registered.                                                                                                                                    |
| `WithDeploymentSlot(string)`       | Deployment slot for App Service resources. Used when deploying to a specific slot (e.g., "staging") rather than production.                                                                                                                                                                                                                                                                                                                                                                                                                   |

## Validate

`Bicep.Validate(resourceGroup, template, options?)` runs `az deployment group validate`; `Bicep.ValidateSubscription(location, template, options?)` runs `az deployment sub validate --location`. A non-zero exit fails the step.

## What-if

`Bicep.WhatIf(resourceGroup, template, options?)` runs `az deployment group what-if --no-pretty-print` and `Bicep.WhatIfSubscription(location, template, options?)` runs `az deployment sub what-if --location`. Both return a `BicepWhatIfResult` that the step fills when it runs. Read it inside execution-time lambdas such as `Ando.Require`, `Ando.Confirm(Func)` and `Ando.When`; reading it earlier throws. Scripts that ignore the return value keep working.

| Member                              | Description                                                                                                                                                                             |
| ----------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Changes`                           | The changes, each with `ResourceId`, `ChangeType` (as az reports it: Create, Delete, Modify, NoChange, Ignore, Deploy, Unsupported) and `PropertyChanges` (number of property changes). |
| `Summary`                           | Counts by type, for example `2 Create, 1 Modify, 30 NoChange`.                                                                                                                          |
| `HasChanges(params string[] types)` | Whether any change has one of the types. With no types: whether anything except NoChange and Ignore would change. Looks at az's `changes` only, not at `potentialChanges`.              |
| `OfType(type)`                      | The changes of one type.                                                                                                                                                                |
| `Json`                              | The raw az output.                                                                                                                                                                      |

The step logs the summary and one line per change that is not NoChange or Ignore. az's raw what-if JSON is not written to the log (it can echo parameter values); stderr is still logged. `HasChanges` and `OfType` look at `changes` only, not at az's `potentialChanges`. A what-if with status `Failed`, or with an `error`, fails the step with az's message.
