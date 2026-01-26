# ANDO - Claude Code Instructions

**Important:** Always address the user as "Mr. Ando".

## GitHub Repository

- **GitHub Username**: `aduggleby`
- **Repository**: `aduggleby/ando`
- **Container Registry**: `ghcr.io/aduggleby/ando-server`

## Git Rules

- **Never automatically git push** - Only push when explicitly asked
- **Only git commit when explicitly asked** - Do not commit changes automatically

## Project Overview

ANDO is a typed C# build system using Roslyn scripting. Build scripts (`build.csando` files) are real C# code executed via Roslyn in Docker containers.

## Key Architecture

- **Roslyn Scripting** (`Microsoft.CodeAnalysis.CSharp.Scripting`) executes `build.csando` files
- **Step Registration Pattern**: Operations (Dotnet, Ef) register steps in a registry, WorkflowRunner executes them
- **Docker Isolation**: All builds run in containers via `DockerManager` and `ContainerExecutor`

## Directory Structure

```
src/Ando/           # Main CLI and library
  Cli/              # CLI entry point
  Config/           # Project configuration (ando.config)
  Context/          # BuildPath, PathsContext, VarsContext
  Execution/        # ProcessRunner, DockerManager, ContainerExecutor
  Logging/          # ConsoleLogger, IBuildLogger
  Operations/       # DotnetOperations, EfOperations, NpmOperations
  References/       # ProjectRef, EfContextRef
  Scripting/        # ScriptHost (Roslyn integration)
  Steps/            # BuildStep, StepRegistry
  Utilities/        # DindChecker, GitHubScopeChecker, SDK ensurers
  Workflow/         # WorkflowRunner, WorkflowConfig
tests/Ando.Tests/   # Test project
  Unit/             # Unit tests (Category=Unit)
  Integration/      # Integration tests (Category=Integration)
  E2E/              # End-to-end tests (Category=E2E)
  TestFixtures/     # Shared MockExecutor, TestLogger
examples/           # Example projects with build.csando files
website/            # Documentation website (see website/CLAUDE.md)
```

## Testing

**IMPORTANT**: When asked to run tests, run ALL tests including Playwright E2E tests.

### .NET Tests
```bash
# Run all .NET tests (unit + integration)
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true

# Run specific test categories
dotnet test --filter "Category=Unit"
dotnet test --filter "Category=Integration"
```

### Playwright E2E Tests
E2E tests use docker-compose to create an isolated network where SQL Server and the server communicate privately. Only the server's web port (5000) is exposed.

```bash
# Install dependencies (required first time)
cd tests/Ando.Server.E2E && npm install

# Run all E2E tests (starts docker-compose automatically)
cd tests/Ando.Server.E2E && npm test

# Run with UI for debugging
cd tests/Ando.Server.E2E && npm run test:ui

# Run headed (visible browser)
cd tests/Ando.Server.E2E && npm run test:headed

# Clean up containers after tests
cd tests && docker compose -f docker-compose.test.yml down
```

### SQL Server for Integration/E2E Tests
- **Integration tests** (.NET): Use Testcontainers to spin up SQL Server automatically
- **E2E tests** (Playwright): Use docker-compose with isolated network
  - `ando-e2e-sqlserver`: SQL Server (no public ports, internal network only)
  - `ando-e2e-server`: Web server (port 17100 exposed)

### Port Allocation
This project uses port range **17100-17199**. Other projects use different ranges to avoid conflicts.

Uses:
- xUnit for testing framework
- FsCheck for property-based testing
- Shouldly for assertions
- MockExecutor for testing without executing commands
- Testcontainers.MsSql for SQL Server integration tests
- Playwright for browser-based E2E tests

## Build Commands

```bash
# Build project
dotnet build

# Run tests
dotnet test tests/Ando.Tests/Ando.Tests.csproj

# Create self-contained executable
dotnet publish src/Ando/Ando.csproj -c Release -r linux-x64 --self-contained -o dist
```

## Ando.Server Deployment (Hetzner)

The Ando.Server is deployed to a Hetzner VPS at `138.199.223.171`. The server runs using rootless Docker under the `ando` user.

### Deployment Steps

```bash
# 1. Build the Docker image locally
docker build -t ando-server:latest -f src/Ando.Server/Dockerfile .

# 2. Push the image to the server (loads into root docker)
docker save ando-server:latest | gzip | ssh -i ~/.ssh/id_claude root@138.199.223.171 "gunzip | docker load"

# 3. Load the image into ando user's rootless docker
ssh -i ~/.ssh/id_claude root@138.199.223.171 "docker save ando-server:latest | sudo -u ando XDG_RUNTIME_DIR=/run/user/1000 DOCKER_HOST=unix:///run/user/1000/docker.sock docker load"

# 4. Restart the services using docker-compose
ssh -i ~/.ssh/id_claude root@138.199.223.171 "sudo -u ando XDG_RUNTIME_DIR=/run/user/1000 DOCKER_HOST=unix:///run/user/1000/docker.sock docker compose -f /opt/ando/docker-compose.yml up -d"

# 5. Verify the deployment
ssh -i ~/.ssh/id_claude root@138.199.223.171 "sudo -u ando XDG_RUNTIME_DIR=/run/user/1000 DOCKER_HOST=unix:///run/user/1000/docker.sock docker ps"
```

### Server Configuration

- **Docker Compose file**: `/opt/ando/docker-compose.yml`
- **Environment config**: `/opt/ando/config/.env`
- **GitHub App key**: `/opt/ando/config/github-app.pem`
- **Data directories**:
  - `/opt/ando/data/artifacts` - Build artifacts
  - `/opt/ando/data/repos` - Cloned repositories
  - `/opt/ando/data/keys` - Data protection keys
  - `/opt/ando/data/sqldata` - SQL Server data

### Important Notes

- The server uses **rootless Docker** under the `ando` user (UID 1000)
- The docker socket is at `/run/user/1000/docker.sock`
- SQL Server runs in a separate container (`ando-sqlserver`)
- The web server listens on `127.0.0.1:8080` (proxied by Caddy)

## Key Interfaces

- `ICommandExecutor`: Interface for executing shell commands
  - `ProcessRunner`: Host execution (for Docker CLI commands)
  - `ContainerExecutor`: Docker exec (for build commands in container)
  - `MockExecutor`: Test double

- `IBuildLogger`: Logging interface
  - `ConsoleLogger`: Production logger
  - `TestLogger`: Test double that captures events

## Code Style

- **Every file must have a header summary comment** (see Documentation Standards below)
- Use records for immutable data
- Prefer async/await for I/O operations
- Operations register steps; they don't execute immediately
- Use fluent builders for options (e.g., `WithConfiguration()`, `WithRuntime()`)

## .NET Version

**IMPORTANT: This project uses .NET 9 exclusively. Do NOT upgrade to .NET 10 or any future version.**

- All projects target `net9.0`
- `global.json` pins the SDK to 9.x versions
- If prompted to upgrade, decline and keep .NET 9

## Keeping Documentation in Sync

**IMPORTANT**: When making changes to the codebase:

1. **Build and run tests first** - Always run `dotnet build` and `dotnet test` to verify changes work before updating documentation
2. **Then update all related documentation and examples**:

1. **Website documentation** (`website/src/data/operations.js`) - Update operation descriptions and examples
2. **Website landing page** (`website/src/pages/index.astro`) - Update the example build.csando section
3. **LLM documentation** (`website/public/llms.txt`) - **ALWAYS update when website content changes** - this is the LLM-friendly reference
4. **Example build.csando files** (`examples/*/build.csando`) - Update all example scripts
5. **Main build.csando** (`build.csando`) - Update if affected
6. **Test files** - Update test assertions and embedded scripts
7. **CLAUDE.md files** - Update if instructions change
8. **CLI help** - Update the `HelpCommand()` method in `AndoCli.cs` when adding or changing CLI commands/options

Run `npm run build` in the website directory to verify documentation builds correctly.

### LLM Documentation (llms.txt)

**CRITICAL**: The `website/public/llms.txt` file must be kept in sync with the website. This file follows the [llms.txt standard](https://llmstxt.org/) and provides a plain-text reference for LLMs. When updating:
- CLI commands or options
- Operations or their signatures
- Examples or common patterns
- Provider documentation

...ensure the same changes are reflected in `llms.txt`.

## CLI Development Rules

**IMPORTANT**: When modifying the ANDO CLI:

1. **Update `ando help`** - The `HelpCommand()` method in `src/Ando/Cli/AndoCli.cs` must always be updated when:
   - Adding new CLI commands
   - Adding new CLI options
   - Changing existing command behavior
   - Adding new operations that affect CLI usage

2. **DIND Mode Detection** - Operations that require Docker-in-Docker are automatically detected by `DindChecker`:
   - Scans registered steps before container creation
   - **Also scans child builds** - Parses `Ando.Build(Directory("..."))` calls and recursively checks child `build.csando` files for DIND operations
   - Checks for `--dind` flag, `ando.config` with `dind: true`, or `ANDO_DIND=1` environment variable
   - **Child builds inherit DIND** - When parent enables DIND, it sets `ANDO_DIND=1` in both the container environment AND the host process environment. The host process env is needed because `Ando.Build` step functions run on the host (they spawn child `ando` processes). This prevents child builds from re-prompting for DIND.
   - Prompts user with options: (Y)es for this run, (a)lways (saves to ando.config), Esc to cancel
   - Current DIND operations: Docker.Build, Docker.Push, Docker.Install, GitHub.PushImage, Playwright.Test

3. **DIND Operations Registry** - When adding new operations that require Docker-in-Docker:
   - Add the operation name to `DindRequiredOperations` in `src/Ando/Utilities/DindChecker.cs`
   - **Also add the operation to `DindOperationPattern` regex** in the same file (for child build scanning)
   - Operations requiring DIND include any that need to run `docker` commands inside the build container
   - Examples: Docker.Build, Docker.Push, Docker.Install, GitHub.PushImage, Playwright.Test (uses docker-compose for E2E tests)

## Documentation Standards

All C# files must include comprehensive documentation using C# comment syntax:

### File Header Comments
Every file must start with a summary block explaining:
- What the file/class does
- Its role in the overall architecture
- Key design decisions

```csharp
// =============================================================================
// FileName.cs
//
// Summary: Brief description of what this file contains and its purpose.
//
// This class/file is responsible for [main responsibility]. It fits into the
// architecture by [architectural role].
//
// Design Decisions:
// - [Key decision and why it was made]
// - [Another decision and rationale]
// =============================================================================
```

### Code Block Comments
Major code blocks (methods, complex logic, important sections) should have explanatory comments:

```csharp
// Validates input parameters before processing.
// We validate early to fail fast and provide clear error messages,
// rather than letting errors propagate through the system.
private void ValidateInput(string path)
{
    // ...
}

// Process items in batches to avoid memory pressure.
// Batch size of 100 was chosen based on benchmarking showing optimal
// throughput vs memory usage tradeoff.
foreach (var batch in items.Chunk(100))
{
    // ...
}
```

### Comment Guidelines
- Use `//` for single-line and short multi-line comments
- Use `///` XML doc comments for public API documentation
- Explain the "why" not just the "what"
- Document design decisions and tradeoffs
- Comment on non-obvious logic or edge case handling
- Keep comments up to date when code changes

### Finding Files Without Headers
Use this command to find C# files missing the required header summary:

```bash
# Find .cs files without the header pattern (excludes obj/bin directories)
find src tests -name "*.cs" -not -path "*/obj/*" -not -path "*/bin/*" | xargs grep -L "^// =\+$" 2>/dev/null
```
