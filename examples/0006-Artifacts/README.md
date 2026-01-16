# 0006-Artifacts Example

This example demonstrates artifact copying using ANDO.

## Prerequisites

- Docker installed and running

## Features Demonstrated

- **Artifacts.CopyToHost()**: Copy build outputs from container to host

## Artifact Operations

When running builds in Docker containers, build outputs exist inside the container.
The Artifacts API allows copying these files back to the host system.

```csharp
// Copy a directory from container to host
Artifacts.CopyToHost("/workspace/dist", "./dist");

// Copy specific files
Artifacts.CopyToHost("/workspace/bin/Release/app", "./output/app");
```

## How It Works

1. Build runs inside a Docker container at `/workspace`
2. Build outputs are created inside the container
3. `Artifacts.CopyToHost()` registers files to copy
4. After successful build, files are copied using `docker cp`

## Running

```bash
ando
```

## Note

This example requires Docker and is not included in automated E2E tests.
