---
title: Running Playwright E2E Tests
description: Add browser-based end-to-end tests to your ANDO build with automatic local/server detection.
difficulty: intermediate
tags:
  - testing
  - playwright
  - docker
  - e2e
---

## Overview

Playwright E2E tests provide powerful browser-based testing for your web applications. However, running them in a CI environment like ANDO requires special consideration because E2E tests often need additional infrastructure (databases, application servers) running in Docker containers.

This recipe shows how to conditionally run Playwright E2E tests based on the build environment, with an override mechanism when you need to force execution.

## The Problem

When ANDO builds run on the CI server, they execute inside Docker containers. If your E2E tests use `docker-compose` to spin up infrastructure (databases, the application under test), you end up with **triple-nested Docker**:

```
Host Machine
└── ANDO Server (Docker container)
    └── Build Container (nested Docker)
        └── E2E Infrastructure (docker-compose) ← This fails!
```

Running Docker-in-Docker-in-Docker is complex and often doesn't work without special configuration. The simplest solution is to skip E2E tests when running on the ANDO server and run them locally where Docker is directly available.

## The Solution

ANDO sets the `ANDO_HOST_ROOT` environment variable when builds run on the CI server. We can use this to detect the environment and conditionally run E2E tests:

- **`ANDO_HOST_ROOT` not set**: Running locally - run E2E tests
- **`ANDO_HOST_ROOT` set**: Running on ANDO server - skip E2E tests by default
- **`-p e2e` profile**: Force E2E tests regardless of environment

## Complete Example

```csharp
// build.csando - Build script with conditional E2E tests
//
// Profiles:
// - (default): Build, test, run E2E locally
// - e2e: Force E2E tests even on ANDO server
//
// Usage:
//   ando              # Runs E2E tests locally, skips on server
//   ando -p e2e       # Forces E2E tests everywhere

// Define profiles
var e2e = DefineProfile("e2e");  // Force E2E tests

// Project references
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
var testProject = Dotnet.Project("./tests/MyApp.Tests/MyApp.Tests.csproj");

// Install SDK and build
Dotnet.SdkInstall();
Dotnet.Restore(project);
Dotnet.Build(project);

// Run .NET unit/integration tests
Dotnet.Test(testProject);

// Detect if running on ANDO server
var isOnServer = Env("ANDO_HOST_ROOT", required: false) != null;

// Run Playwright E2E tests conditionally
var e2eTests = Directory("./tests/MyApp.E2E");
if (!isOnServer || e2e)
{
    // E2E tests require docker-compose for infrastructure
    // This works locally but not in nested Docker without special setup
    Log.Info("Running Playwright E2E tests...");
    Npm.Ci(e2eTests);
    Playwright.Install(e2eTests);
    Playwright.Test(e2eTests);
}
else
{
    Log.Info("Skipping E2E tests (running on ANDO server - use -p e2e to override)");
}
```

## How It Works

### Environment Detection

The `Env()` function retrieves environment variables. With `required: false`, it returns `null` instead of throwing when the variable isn't set:

```csharp
var isOnServer = Env("ANDO_HOST_ROOT", required: false) != null;
```

### Profile Override

The `DefineProfile()` function creates a boolean that becomes `true` when you activate it with `-p`:

```csharp
var e2e = DefineProfile("e2e");

// e2e is false by default
// e2e is true when you run: ando -p e2e
```

### Conditional Logic

The condition `!isOnServer || e2e` means:
- Run E2E tests if NOT on server (local development)
- OR run E2E tests if the `e2e` profile is active (explicit override)

## Running the Build

```bash
# Local development - E2E tests run automatically
ando

# On ANDO server - E2E tests are skipped
# (automatically detected via ANDO_HOST_ROOT)

# Force E2E tests on ANDO server
ando -p e2e

# Combine with other profiles
ando -p push,e2e
```

## Important: Exit Code Handling

**The build will fail if tests fail** - but only if exit codes are properly propagated.

`Playwright.Test()` uses `npx playwright test` directly, which correctly returns a non-zero exit code when tests fail. ANDO checks the exit code and stops the build on failure.

**Avoid npm scripts that swallow errors:**

```json
// BAD - This will NOT fail the build when tests fail!
"scripts": {
  "test": "playwright test || true"
}
```

If you must use an npm script, ensure it propagates the exit code:

```json
// GOOD - Exit code is preserved
"scripts": {
  "test": "playwright test"
}
```

To use a custom npm script instead of `npx playwright test`:

```csharp
// Use Npm.Run() only if your script properly propagates exit codes
Npm.Run(e2eTests, "test");

// Or use Playwright.Test() with UseNpmScript option
Playwright.Test(e2eTests, o => o.UseNpmScript = true);
```

## Key Operations Used

| Operation | Description |
|-----------|-------------|
| `Env(name, required?)` | Get environment variable. Use `required: false` to return null if not set. |
| `DefineProfile(name)` | Define a build profile for conditional execution. |
| `Directory(path)` | Create a directory reference for npm/Playwright operations. |
| `Npm.Ci(directory)` | Install npm dependencies using clean install. |
| `Playwright.Install(directory)` | Install Playwright browsers. |
| `Playwright.Test(directory)` | Run Playwright tests (fails build on test failures). |
| `Log.Info(message)` | Log an informational message. |

## See Also

- [Npm Provider](/providers/npm) - npm operations reference
- [Playwright Provider](/providers/playwright) - Playwright operations reference
- [Profiles](/cli#profiles) - CLI profile documentation
