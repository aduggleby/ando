---
title: Npm
description: Run npm commands for JavaScript/TypeScript projects.
provider: Npm
---

## Example

Build a frontend application.

```csharp
// Create a directory reference
var frontend = Directory("./frontend");

// Install dependencies (prefer ci for reproducible builds)
Npm.Ci(frontend);

// Run linting
Npm.Run(frontend, "lint");

// Run tests
Npm.Test(frontend);

// Build for production
Npm.Build(frontend);
```

## Timeouts

Npm operations use ANDO's standard command timeout. Each `Npm.Install`, `Npm.Ci`, `Npm.Run`, `Npm.Test`, and `Npm.Build` command has 5 minutes to finish. If the command exceeds that limit, ANDO stops the process tree and the failed step log reports the timeout, for example `Command timed out after 300000ms`.

When the same script runs on the ANDO CI Server, the server also applies a whole-build timeout. The default is 15 minutes and the maximum is 60 minutes, configurable through project settings and server `Build__DefaultTimeoutMinutes` / `Build__MaxTimeoutMinutes` settings. A single npm command can hit the 5-minute command timeout before the overall build timeout is reached.
