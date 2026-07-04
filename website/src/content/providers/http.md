---
title: HTTP
description: Gate a deploy on an HTTP health check by polling an endpoint until it is healthy.
provider: Http
---

## Example

Wait for a freshly deployed app to report healthy before swapping a slot into
production. `Http.WaitForHealthy` runs in-process on the build host, so the
build image does not need `curl`.

```csharp
// Deploy to the staging slot
AppService.DeployFolder("my-web-app", Root / "publish", "my-rg", o => o
  .WithDeploymentSlot("staging"));

// Wait for the staging slot to come up before promoting it
Http.WaitForHealthy("https://my-web-app-staging.azurewebsites.net/healthz", o => o
  .WithTimeoutSeconds(300)
  .WithIntervalSeconds(5));

// Promote staging to production
AppService.SwapSlots("my-web-app", "staging", "my-rg");
```

## Options Reference

### Http.WaitForHealthy Options

| Option | Description |
|--------|-------------|
| `WithExpectedStatus(int)` | HTTP status code that marks the endpoint healthy. Default `200`. |
| `WithTimeoutSeconds(int)` | Total time to keep polling before the step fails. Default `300`. |
| `WithIntervalSeconds(int)` | Delay between polls. Default `5`. |
| `WithRequestTimeoutSeconds(int)` | Per-request timeout for each poll. Default `10`. |
