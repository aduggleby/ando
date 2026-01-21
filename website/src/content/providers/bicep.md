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
