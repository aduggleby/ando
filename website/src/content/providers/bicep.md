---
title: Bicep
description: Deploy Azure infrastructure using Bicep templates.
provider: Bicep
---

## Example

Deploy infrastructure and use outputs in subsequent steps.

```csharp
// Preview changes first
Bicep.WhatIf("my-app-rg", "./infra/main.bicep", o => o
  .WithParameterFile("./infra/params.prod.json"));

// Deploy - returns BicepDeployment with typed output access
var deployment = Bicep.DeployToResourceGroup("my-app-rg", "./infra/main.bicep", o => o
  .WithParameterFile("./infra/params.prod.json"));

// Pass deployment outputs to other operations
Ef.DatabaseUpdate(DbContext, deployment.Output("sqlConnectionString"));

// Build Bicep to ARM for validation
Bicep.Build("./infra/main.bicep", "./artifacts/main.json");
```

## Options Reference

### Bicep.DeployToResourceGroup / DeployToSubscription / WhatIf Options

| Option | Description |
|--------|-------------|
| `WithName(string)` | Custom deployment name. Defaults to the template filename. Useful for tracking deployments in Azure Portal activity log. |
| `WithParameterFile(string)` | Path to parameters file. Supports both JSON parameter files (.json) and Bicep parameter files (.bicepparam). |
| `WithParameter(name, value)` | Add an inline parameter value. Overrides values from parameter files. Can be called multiple times for multiple parameters. |
| `WithMode(DeploymentMode)` | Deployment mode: `DeploymentMode.Incremental` (default) adds/updates resources but doesn't delete missing ones. `DeploymentMode.Complete` deletes resources not in the template (use with caution). |
| `WithDeploymentSlot(string)` | Deployment slot for App Service resources. Used when deploying to a specific slot (e.g., "staging") rather than production. |
