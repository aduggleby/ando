# ANDO - Claude Code Instructions

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
```

## Testing

```bash
# Run unit tests only
dotnet test --filter "Category=Unit"

# Run all tests
dotnet test

# Run with coverage
dotnet test /p:CollectCoverage=true
```

Uses:
- xUnit for testing framework
- FsCheck for property-based testing
- Shouldly for assertions
- MockExecutor for testing without executing commands

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

- Use records for immutable data
- Prefer async/await for I/O operations
- Operations register steps; they don't execute immediately
- Use fluent builders for options (e.g., `WithConfiguration()`, `WithRuntime()`)

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
