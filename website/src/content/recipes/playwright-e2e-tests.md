---
title: Playwright E2E Tests in ANDO Builds
description: Run browser-based end-to-end tests inside ANDO builds using Docker-in-Docker mode.
difficulty: intermediate
tags:
  - testing
  - playwright
  - docker
  - e2e
---

## Overview

Playwright E2E tests provide powerful browser-based testing for web applications. This recipe shows how to run them inside ANDO builds using Docker-in-Docker (sibling containers) mode.

## The Challenge

E2E tests typically use `docker-compose` to spin up test infrastructure (databases, application servers). Running this inside an ANDO build container creates nested Docker execution:

```
Host Machine
└── ANDO Build Container
    └── docker-compose (E2E Infrastructure) ← Doesn't work by default!
```

## The Solution: Docker-in-Docker Mode

ANDO's `--dind` flag enables Docker-in-Docker mode, giving the build container access to the host's Docker daemon. Containers started by `docker-compose` become **siblings** to the build container rather than nested inside it:

```
Host Machine (Docker Daemon)
├── ANDO Build Container (runs tests)
├── SQL Server Container (sibling)
└── Web Server Container (sibling)
```

## Implementation

### 1. Container Detection in Tests

Your Playwright configuration needs to detect when running inside a container and connect via `host.docker.internal` instead of `localhost`:

```typescript
// playwright.config.ts
import * as fs from 'fs';

function isInsideContainer(): boolean {
  return fs.existsSync('/.dockerenv');
}

function getBaseUrl(): string {
  const port = 17100;
  const host = isInsideContainer() ? 'host.docker.internal' : 'localhost';
  return `http://${host}:${port}`;
}

export default defineConfig({
  use: {
    baseURL: process.env.BASE_URL || getBaseUrl(),
  },
  // ... rest of config
});
```

Apply the same pattern in your `global-setup.ts` for health checks:

```typescript
// global-setup.ts
function getServerUrl(): string {
  const port = 17100;
  const host = isInsideContainer() ? 'host.docker.internal' : 'localhost';
  return `http://${host}:${port}/health`;
}
```

### 2. Build Script Integration

Add E2E tests to your `build.csando` with environment and Docker detection:

```csharp
// Define e2e profile to force tests on server
var e2e = DefineProfile("e2e");

// Detect if running on Ando.Server
var isOnServer = Env("ANDO_HOST_ROOT", required: false) != null;

// Run E2E tests (locally or with -p e2e override)
var e2eTests = Directory("./tests/MyApp.E2E");
var shouldRunE2E = !isOnServer || e2e;

if (shouldRunE2E)
{
    // Check if Docker is available (requires --dind flag)
    if (!Docker.IsAvailable())
    {
        Log.Warning("Skipping E2E tests (Docker not available - run with 'ando --dind')");
    }
    else
    {
        Log.Info("Running Playwright E2E tests...");

        // Install Docker CLI (needed for docker-compose)
        Docker.Install();

        // Install npm dependencies
        Npm.Ci(e2eTests);

        // Install Playwright browsers
        Playwright.Install(e2eTests);

        // Run E2E tests
        Playwright.Test(e2eTests);
    }
}
else
{
    Log.Info("Skipping E2E tests (use -p e2e to override)");
}
```

The `Docker.IsAvailable()` check runs immediately (not as a registered step) and returns `true` if Docker CLI and daemon are accessible. This allows the build to gracefully skip E2E tests with a warning when Docker isn't available, rather than failing with a cryptic error.

### 3. Running the Build

```bash
# Run locally with E2E tests (requires --dind for Docker access)
ando --dind

# Skip E2E tests (runs faster)
ando

# Force E2E tests on server
ando -p e2e --dind
```

## Running Tests Locally (Outside ANDO)

You can still run tests directly on your machine for debugging with the Playwright UI:

```bash
# Install dependencies
cd tests/MyApp.E2E && npm install

# Run tests with UI (great for debugging)
npm run test:ui

# Run tests headed (visible browser)
npm run test:headed

# Run tests in CI mode
npm test
```

When running outside an ANDO container, `isInsideContainer()` returns false and tests connect to `localhost` as usual.

## CI Integration

For CI pipelines, you have two options:

### Option 1: E2E Tests Inside ANDO Build

```yaml
# GitHub Actions
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Run ANDO build with E2E tests
        run: ando --dind
```

### Option 2: E2E Tests as Separate Job

```yaml
jobs:
  build:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - name: Run ANDO build (no E2E)
        run: ando

  e2e:
    runs-on: ubuntu-latest
    needs: build
    steps:
      - uses: actions/checkout@v4
      - name: Run E2E tests
        run: |
          cd tests/MyApp.E2E
          npm ci
          npx playwright install --with-deps
          npm test
```

## Exit Code Handling

Ensure your npm scripts properly propagate exit codes:

```json
// GOOD - Exit code is preserved
"scripts": {
  "test": "playwright test"
}

// BAD - This will NOT fail CI when tests fail!
"scripts": {
  "test": "playwright test || true"
}
```

ANDO correctly propagates exit codes from Playwright.Test() - if tests fail, the build fails.

## Key Takeaways

| Scenario | Command | E2E Tests |
|----------|---------|-----------|
| Local development | `ando --dind` | Run |
| Local (fast build) | `ando` | Skip |
| Server (default) | CI pipeline | Skip |
| Server (forced) | `ando -p e2e --dind` | Run |
| Debug with UI | `npm run test:ui` | Run locally |

## How It Works

1. **`--dind` flag** mounts the host's Docker socket into the build container
2. **`Docker.Install()`** installs the Docker CLI inside the container
3. **docker-compose** starts sibling containers on the host's Docker daemon
4. **Container detection** switches URLs from `localhost` to `host.docker.internal`
5. **Playwright tests** connect to the server container via the host network

## See Also

- [Playwright Provider](/providers/playwright) - Playwright operations reference
- [Npm Provider](/providers/npm) - npm operations reference
- [Docker Provider](/providers/docker) - Docker operations and `--dind` mode
