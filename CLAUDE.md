# ANDO - Claude Code Instructions

**Important:** Always address the user as "Mr. Ando".

## Project Overview

ANDO is a typed C# build system using Roslyn scripting. Build scripts (`build.ando` files) are real C# code executed via Roslyn in Docker containers.

## Key Architecture

- **Roslyn Scripting** (`Microsoft.CodeAnalysis.CSharp.Scripting`) executes `build.ando` files
- **Step Registration Pattern**: Operations (Dotnet, Ef) register steps in a registry, WorkflowRunner executes them
- **Docker Isolation**: Builds run in containers via `DockerManager` and `ContainerExecutor`
- **Local Mode**: `--local` flag skips Docker for faster iteration

## Directory Structure

```
src/Ando/           # Main CLI and library
  Cli/              # CLI entry point
  Context/          # BuildPath, PathsContext, VarsContext
  Execution/        # ProcessRunner, DockerManager, ContainerExecutor
  Logging/          # ConsoleLogger, IBuildLogger
  Operations/       # DotnetOperations, EfOperations, NpmOperations
  References/       # ProjectRef, EfContextRef
  Scripting/        # ScriptHost (Roslyn integration)
  Steps/            # BuildStep, StepRegistry
  Workflow/         # WorkflowRunner, WorkflowConfig
tests/Ando.Tests/   # Test project
  Unit/             # Unit tests (Category=Unit)
  Integration/      # Integration tests (Category=Integration)
  E2E/              # End-to-end tests (Category=E2E)
  TestFixtures/     # Shared MockExecutor, TestLogger
examples/           # Example projects with build.ando files
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

## Key Interfaces

- `ICommandExecutor`: Interface for executing shell commands
  - `ProcessRunner`: Local execution
  - `ContainerExecutor`: Docker exec
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

## Keeping Documentation in Sync

**IMPORTANT**: When making changes to the codebase:

1. **Build and run tests first** - Always run `dotnet build` and `dotnet test` to verify changes work before updating documentation
2. **Then update all related documentation and examples**:

1. **Website documentation** (`website/src/data/operations.js`) - Update operation descriptions and examples
2. **Website landing page** (`website/src/pages/index.astro`) - Update the example build.ando section
3. **Example build.ando files** (`examples/*/build.ando`) - Update all example scripts
4. **Main build.ando** (`build.ando`) - Update if affected
5. **Test files** - Update test assertions and embedded scripts
6. **CLAUDE.md files** - Update if instructions change

Run `npm run build` in the website directory to verify documentation builds correctly.

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
