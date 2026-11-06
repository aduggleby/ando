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

## Overloads

`Http.WaitForHealthy` accepts the URL in four forms:

| Overload                                                     | Use                                                               |
| ------------------------------------------------------------ | ----------------------------------------------------------------- |
| `WaitForHealthy(string url, configure?)`                     | One URL.                                                          |
| `WaitForHealthy(IEnumerable<string> urls, configure?)`       | Several URLs. A check passes only when every URL passes.          |
| `WaitForHealthy(Func<string> url, configure?)`               | A URL computed at execution time, for example from a step output. |
| `WaitForHealthy(Func<IEnumerable<string>> urls, configure?)` | Several URLs computed at execution time.                          |

```csharp
var host = AppService.GetHostName("my-web-app", "my-rg", "staging");

Http.WaitForHealthy(
  () => new[] { $"https://{host.Value}/healthz", $"https://{host.Value}/healthz/db" },
  o => o
    .ConsecutiveSuccesses(3)
    .MinGapSeconds(11)
    .WithTimeoutSeconds(600)
    .ExpectJson("version", "1.4.0"));
```

## Options Reference

### Http.WaitForHealthy Options

| Option                                     | Description                                                                                                                                                                                                                                                                    |
| ------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| `WithExpectedStatus(int)`                  | HTTP status code that marks the endpoint healthy. Default `200`.                                                                                                                                                                                                               |
| `WithTimeoutSeconds(int)`                  | Total time to keep polling before the step fails. Default `300`.                                                                                                                                                                                                               |
| `WithIntervalSeconds(int)`                 | Delay between polls. Default `5`.                                                                                                                                                                                                                                              |
| `WithRequestTimeoutSeconds(int)`           | Per-request timeout for each poll. Default `10`.                                                                                                                                                                                                                               |
| `ConsecutiveSuccesses(int)`                | Number of passing checks in a row required. A failure resets the count. With several URLs, one check means every URL passed. Default `1`.                                                                                                                                      |
| `MinGapSeconds(int)`                       | Minimum seconds between two counted passing checks. `ConsecutiveSuccesses(3).MinGapSeconds(11)` means three passes spread over at least 22 seconds. Default `0`.                                                                                                               |
| `ExpectJson(string path, string expected)` | Requires the response body to be JSON with `expected` at the dotted `path` (such as `version` or `build.commit`). Numbers and booleans compare by their JSON text. Applies to every URL of the call; use a separate call for URLs that return something else. Can be repeated. |

## Http.Get

`Http.Get(url, allowAnyStatus = false)` and `Http.Get(Func<string> url, allowAnyStatus = false)` send a GET request from the build host and return a `StepOutput`. `Value` is the response body and `StatusCode` the status code. The step fails on a non-2xx status unless `allowAnyStatus` is true. Reading the output before the step ran throws.

```csharp
var health = Http.Get("https://eu.example.com/healthz");
Ando.Report("eu.healthz", health);
```

## Http.PublicIpV4

`Http.PublicIpV4()` looks up this machine's public IPv4 address, as seen by public services, and returns a `StepOutput` whose `Value` is the address.

```csharp
var ip = Http.PublicIpV4();
Ando.Report("buildIp", ip);
```
