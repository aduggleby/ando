# ANDO — Typed C# Build System

## 1. Overview

**ANDO** is a strongly typed, C#-native build system that lets developers define build, test, publish, and deployment workflows using *plain C#* — without YAML, DSLs, or string-based command definitions.

ANDO executes a single `build.ando` file that:
- Feels like normal C# top-level statements
- Uses strongly typed APIs for common operations
- Avoids boilerplate (`using`, `Main`, `Execute`)
- Provides structured access to filesystem paths, variables, and environment data
- Runs deterministically and safely across local and CI environments

---

## 2. Goals & Non-Goals

### Goals
1. **Zero-config authoring**
   - No `using` statements
   - No `Main()` or `Execute()` calls
   - Single-file build definition

2. **Strong typing**
   - Typed project references, paths, Azure targets, EF contexts
   - IntelliSense-first developer experience
   - No stringly-typed build commands

3. **Low ceremony**
   - Top-level statements
   - Imperative, readable flow
   - No schema files or DSL syntax

4. **Extensible**
   - New domains (Docker, Kubernetes, AWS, etc.) via namespaces
   - No breaking changes to existing build files

5. **Host-agnostic**
   - Runs locally, in CI, or in containers
   - No dependency on IDE features

### Non-Goals
- Replacing MSBuild internals
- Supporting arbitrary scripting languages
- Providing a visual workflow editor
- Runtime dependency on application assemblies

---

## 3. Authoring Experience

### Example `build.ando`

```csharp
// Project references
var WebApi = Project.From("./src/WebApi/WebApi.csproj");
var Frontend = Project.From("./src/Frontend");  // Node.js project

// Tool configuration
var EfTool = Dotnet.Tool("dotnet-ef", version: "8.0.0");
var NodeTool = Node.Use(version: "20.11.0");

// EF context
var MainDb = Ef.DbContextFrom(WebApi);

// Workflow configuration
Workflow("ci", w =>
{
  w.Configuration = Configuration.Release;
  w.Verbosity = Verbosity.Minimal;
});

// Access context
Context.Vars["environment"] =
  Context.Vars.Env("ASPNETCORE_ENVIRONMENT") ?? "Production";

// Backend build
Dotnet.Restore(WebApi);
Dotnet.Build(WebApi);
Dotnet.Test(WebApi);

// Frontend build
Npm.Ci();
Npm.Run("build");
Npm.Run("test");

// Publish
Dotnet.Publish(WebApi, o => o
  .Output(Context.Paths.Artifacts / "publish" / "api"));

// Database migration
Ef.DatabaseUpdate(MainDb);
```

---

## 4. Core Concepts

### 4.1 Build Script
- Single C# file: `build.ando`
- Compiled and executed by the ANDO runner
- Uses top-level statements
- No user-defined entry point

---

## 5. Runner-Injected Globals

### Injected Namespaces
The runner automatically injects commonly used namespaces:

```
Build
Build.Dotnet
Build.Azure
Build.Ef
Build.IO
```

### Injected Global Objects

```csharp
BuildContext Context;   // Access via Context.Paths and Context.Vars
```

### Lifecycle Footer
The runner appends the execution footer automatically:

```csharp
return BuildRuntime.Execute(args);
```

---

## 6. Context Object

ANDO provides a single `Context` object with access to paths, variables, and build state.

### 6.1 `Context.Paths` — Filesystem Context

Purpose:
- Typed access to directories and files
- Avoid raw string paths

Common properties:
- `Root` — Directory containing `build.ando`
- `Src` — Source directory (`<root>/src`)
- `Artifacts` — Build outputs (`<root>/artifacts`)
- `Temp` — Temporary files (`<root>/.ando/tmp`)

Supports path composition:

```csharp
Context.Paths.Artifacts / "publish" / "webapi"
```

### 6.2 `Context.Vars` — Variables & Environment

Purpose:
- Shared build state
- Access environment variables
- Handle secrets safely

Capabilities:
- Key/value storage
- Read environment variables
- Enforce required variables

```csharp
Context.Vars["buildNumber"] = "123";
Context.Vars.Env("CI");
Context.Vars.EnvRequired("SQL_CONNECTION");
```

---

## 7. Strongly Typed References

### 7.1 Project Reference

```csharp
var WebApi = Project.From("./src/WebApi/WebApi.csproj");
```

Represents a buildable .NET project without loading assemblies.

---

### 7.2 EF Context Reference

```csharp
var MainDb = Ef.DbContextFrom(WebApi);
var ReportingDb = Ef.DbContextFrom(WebApi, "Reporting");
```

Represents an EF Core design-time context bound to a project.

---

### 7.3 Azure Web App Reference

```csharp
var app = Azure.WebApp(subscription, resourceGroup, name);
```

Represents a deployment target without requiring Azure SDK dependencies at authoring time.

---

## 8. Operations (Steps)

Each operation:
- Registers a build step
- Is executed later by the runtime
- Runs deterministically

### Dotnet Operations

```csharp
Dotnet.Restore(Project);
Dotnet.Build(Project);
Dotnet.Test(Project);
Dotnet.Publish(Project, options);
```

### EF Operations

```csharp
Ef.DatabaseUpdate(EfContextRef);
Ef.AddMigration(EfContextRef, name);
Ef.Script(EfContextRef, outputFile);
```

### Azure Operations (Future)

```csharp
Azure.WebAppDeploy(WebAppRef, options);
```

### Npm Operations

```csharp
// Ensure Node.js is available (installs to .ando/tools if needed)
var NodeTool = Node.Use(version: "20.11.0");  // or Node.UseLatestLts()

// Run npm commands
Npm.Install();
Npm.Run("build");
Npm.Run("test");
Npm.Ci();  // Clean install for CI
```

### Tool Operations

```csharp
// .NET CLI tools
var EfTool = Dotnet.Tool("dotnet-ef", version: "8.0.0");
var FormatTool = Dotnet.Tool("dotnet-format");

// Use tools in operations
Ef.DatabaseUpdate(MainDb);  // Uses locally installed dotnet-ef
```

---

## 9. Execution Model

### 9.1 Overview

- **All workflow execution happens inside a Docker container**
- Steps are executed sequentially by default
- Fail-fast on errors (unless `--continue-on-error` is specified)
- Real-time output streaming to the logger
- Clear, structured logging per step

Future extensions:
- Dependency graph (`DependsOn`, `After`)
- Parallel execution
- Incremental caching

### 9.2 Docker-Based Execution

ANDO requires Docker and executes all workflows inside an isolated container. This provides:

- **Complete isolation**: Builds don't affect host system
- **Reproducibility**: Consistent environment across machines
- **Clean state**: Artifacts scrubbed between runs
- **CI-friendly**: Works identically locally and in CI pipelines

#### Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│                         Host Machine                             │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                      ANDO CLI                              │  │
│  │  - Parses build.ando                                      │  │
│  │  - Manages Docker container lifecycle                     │  │
│  │  - Streams output from container                          │  │
│  └───────────────────────────────────────────────────────────┘  │
│                              │                                   │
│                              │ docker exec / docker run          │
│                              ▼                                   │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │                   ANDO Build Container                     │  │
│  │  ┌─────────────────────────────────────────────────────┐  │  │
│  │  │  Base: Alpine/Debian + .NET SDK                     │  │  │
│  │  │  + Installed tools (Node.js, dotnet-ef, etc.)       │  │  │
│  │  └─────────────────────────────────────────────────────┘  │  │
│  │                                                           │  │
│  │  Mounts:                                                  │  │
│  │    /workspace        ← Project directory (bind mount)    │  │
│  │    /workspace/.ando/cache/nuget  ← NuGet cache           │  │
│  │    /workspace/.ando/cache/npm    ← npm cache             │  │
│  │    /var/run/docker.sock ← Docker socket (for DinD)       │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

#### Docker Requirement

Docker is **required** for ANDO to function. If Docker is not available:

1. ANDO displays clear installation instructions for the current OS
2. Offers to install Docker automatically (with user confirmation)
3. Provides links to Docker Desktop / Docker Engine documentation

```
✖ Docker is not available

ANDO requires Docker to run workflows in isolated containers.

Install Docker:
  macOS:   brew install --cask docker
  Ubuntu:  curl -fsSL https://get.docker.com | sh
  Windows: winget install Docker.DockerDesktop

Or visit: https://docs.docker.com/get-docker/

Would you like ANDO to install Docker automatically? [y/N]
```

#### Container Lifecycle

1. **Single container per workflow**: One container is created and reused for all steps
2. **Warm container option**: Container can be kept alive between workflow runs for faster iteration
3. **Artifact scrubbing**: `artifacts/` directory is cleaned at workflow start
4. **Tool persistence**: Installed tools remain in warm containers

```
Workflow Run 1:
  Container created → Tools installed → Steps execute → Container kept warm

Workflow Run 2 (warm):
  Container reused → Tools already present → Steps execute (faster)

Workflow Run 3 (cold):
  Container recreated → Tools reinstalled → Steps execute
```

#### Container Modes

| Mode | Command | Behavior |
|------|---------|----------|
| Default | `ando run` | Create new container, keep warm after completion |
| Cold | `ando run --cold` | Always create fresh container |
| Warm | `ando run --warm` | Reuse existing container if available |
| Clean | `ando clean --container` | Remove the project's warm container |

#### Docker-in-Docker (DinD) Support

ANDO supports running inside a container (e.g., in CI) by:

1. Mounting the Docker socket (`/var/run/docker.sock`)
2. Using Docker's `--privileged` flag when needed
3. Supporting rootless Docker configurations

```yaml
# Example: Running ANDO in GitLab CI
build:
  image: ando:latest
  services:
    - docker:dind
  script:
    - ando run ci
```

### 9.3 Process Execution Inside Container

Commands are executed inside the container via `docker exec`:

```
┌─────────────────────────────────────────────────────────────────┐
│                        Operations Layer                          │
│  DotnetOperations    NpmOperations    EfOperations    ...       │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                     ContainerExecutor                            │
│                                                                  │
│  - Wraps commands in `docker exec <container>`                  │
│  - Real-time stdout/stderr streaming                            │
│  - Exit code interpretation                                      │
│  - Working directory management (inside container)              │
│  - Environment variable injection                                │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
                    docker exec <container> <command>
```

#### Execution Behavior

1. **Real-time streaming**: stdout and stderr streamed line-by-line as they arrive
2. **No output parsing**: Errors displayed as-is from the tool
3. **Fail-fast**: Non-zero exit code stops the workflow
4. **Exit code mapping**: Exit code 0 = success, any other = failure

### 9.4 Caching Strategy

Caches are stored in `.ando/cache/` and mounted into the container for performance:

```
<root>/
├── .ando/
│   ├── cache/
│   │   ├── nuget/          # NuGet packages cache
│   │   │   └── packages/
│   │   └── npm/            # npm cache
│   │       └── _cacache/
│   ├── tools/              # Installed tools (inside container)
│   └── container/          # Container metadata
│       └── container-id
├── artifacts/              # Build outputs (scrubbed each run)
└── build.ando
```

#### Cache Configuration

```csharp
// In build.ando - caches are automatic, but can be configured
Cache.NuGet();              // Enable NuGet cache (default: on)
Cache.Npm();                // Enable npm cache (default: on)
Cache.Custom("gradle", paths.Root / ".gradle");  // Custom cache
```

### 9.5 Tool Management

Tools are installed **inside the container** on first use:

#### Base Image

ANDO uses a minimal base image with .NET SDK pre-installed:

```dockerfile
# ando-base image (conceptual)
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine
# Minimal additional setup for ANDO
```

#### Tool Installation Inside Container

```csharp
// .NET CLI tools - installed via dotnet tool install
var EfTool = Dotnet.Tool("dotnet-ef", version: "8.0.0");

// Node.js - downloaded and installed in container
var NodeTool = Node.Use(version: "20.11.0");
```

#### Tool Persistence

- **Warm container**: Tools persist between workflow runs
- **Cold container**: Tools reinstalled (but cached downloads speed this up)
- **Custom image**: User can provide pre-built image with tools

#### Supported Tool Sources

| Tool Type | Installation Method | Example |
|-----------|---------------------|---------|
| .NET Tools | `dotnet tool install --tool-path` | `dotnet-ef`, `dotnet-format` |
| Node.js | Direct download from nodejs.org | `node`, `npm`, `npx` |
| System packages | `apk add` (Alpine) / `apt install` (Debian) | `git`, `curl` |

---

## 10. Error Handling & Diagnostics

### 10.1 Error Behavior

- **Fail-fast by default**: First failed step stops the workflow
- **No error parsing**: Raw tool output is displayed as-is
- **Exit code determines failure**: Non-zero exit code = step failure
- **Optional continue-on-error**: `--continue-on-error` flag to proceed after failures

### 10.2 Error Output

When a step fails:
1. The step is marked as failed with duration
2. The last N lines of output are highlighted (if not already streamed)
3. The workflow stops (unless `--continue-on-error`)
4. Exit code 1 is returned

```
▶ Dotnet.Build (WebApi) ...
  Building /src/WebApi/WebApi.csproj
  error CS1002: ; expected
  error CS1022: Type or namespace definition expected
✖ Dotnet.Build (WebApi) 00:02.341 - Process exited with code 1

Workflow 'ci' failed: 1/3 steps failed
```

### 10.3 Diagnostic Information

- Step name and context always included
- Full command shown in `--verbosity detailed` mode
- Stack traces hidden by default (opt-in with `--verbosity detailed`)

---

## 11. Security Considerations

- Secrets accessed only via `ContextVars.Env*`
- No implicit environment variable dumping
- Optional secret masking in logs
- No reflection into application assemblies

---

## 12. Extensibility Model

New domains (e.g. Docker) can be added via namespaces:

```csharp
Docker.Build(image);
Docker.Push(image);
```

No changes to the ANDO core runner are required.

---

## 13. Implementation Strategy

### Phase 1 — MVP (Complete)
- Roslyn-based script compilation
- Context injection (ContextPaths, ContextVars)
- Step registration and sequential execution
- Logging infrastructure
- CLI with `run`, `version`, `help` commands

### Phase 2 — Docker Execution
- **Docker Container Management**
  - Container creation and lifecycle management
  - Warm container support for faster iteration
  - Docker-in-Docker (DinD) support
  - Docker installation detection and guidance
- **ContainerExecutor**: Execute commands inside container
  - `docker exec` wrapper with real-time streaming
  - Exit code handling
  - Working directory and environment support
- **Caching Strategy**
  - NuGet cache in `.ando/cache/nuget/`
  - npm cache in `.ando/cache/npm/`
  - Mounted into container for performance
- **Tool Management**: Install tools inside container
  - .NET tools via `dotnet tool install`
  - Node.js via direct download
  - Tool persistence in warm containers
- **Dotnet Operations**: Real execution
  - `dotnet restore`, `build`, `test`, `publish`
  - Configuration and verbosity passthrough
- **Npm Operations**: Real execution
  - `npm install`, `npm run`, `npm ci`
  - Uses Node.js installed in container

### Phase 3 — DX Improvements
- Rich logging with better formatting
- Failure summaries
- `ando doctor` command
- `ando init` command

### Phase 4 — Advanced
- Dependency graph (`DependsOn`, `After`)
- Parallelism
- Incremental caching
- Plugin system

---

## 14. Success Metrics

- Typical build files under 150 lines
- Zero YAML required
- Autocomplete-driven authoring
- No runtime reference to app assemblies
- Build scripts readable in code review

---

## 15. Design Principle

> **If it doesn't feel like normal C#, it's the wrong abstraction.**

ANDO succeeds when developers forget they're using a build tool at all—and simply feel like they're writing a small, well-typed C# program.

---

## 16. CLI UX Specification

### 16.1 Overview

ANDO is delivered as a cross-platform CLI (`ando`) distributed as a .NET tool (global or local) and/or a standalone binary. The CLI is responsible for:

- Locating and executing `build.ando`
- Injecting runner globals/usings and appending the execution footer
- Providing a consistent interface for running targets/steps locally and in CI
- Emitting human-friendly logs by default, with structured output options
- Performing environment and dependency checks (`dotnet`, Azure CLI, EF tooling, etc.)

### 16.2 Command Summary

| Command | Purpose |
|---|---|
| `ando run` | Run the build workflow (default command) |
| `ando list` | List workflows, jobs, and steps discovered in `build.ando` |
| `ando init` | Create a starter `build.ando` and optional config |
| `ando doctor` | Diagnose environment/tooling prerequisites |
| `ando clean` | Delete artifacts/temp caches produced by ANDO |
| `ando env` | Print resolved paths/vars/env used by the runner |
| `ando version` | Print CLI + runtime version info |

> Note: ANDO does not require a separate project file. Configuration is inferred from repository layout and optional `.ando/config.toml` (future).

### 16.3 `ando run`

**Primary workflow execution command.** If no arguments are provided, ANDO runs the default workflow in `build.ando` (typically the first declared workflow, or one explicitly marked default).

#### Usage
```bash
ando run [workflow] [options] [-- <args passed to build>]
```

#### Examples
```bash
ando run
ando run ci
ando run ci --configuration Release
ando run ci --dry-run
ando run ci --only Dotnet.Publish
ando run ci --skip Ef.DatabaseUpdate
ando run ci --format json
ando run ci -- --some-custom-arg value
```

#### Options
- `--configuration <Debug|Release>`: Overrides workflow default configuration.
- `--verbosity <quiet|minimal|normal|detailed>`: Controls logging verbosity.
- `--dry-run`: Prints the execution plan without running steps.
- `--only <selector>`: Run only matching steps (supports wildcards).
- `--skip <selector>`: Skip matching steps (supports wildcards).
- `--continue-on-error`: Continue executing subsequent steps after a failure (default: off).
- `--max-parallel <n>`: Max parallel steps when parallelism is enabled (future; default 1).
- `--format <text|json>`: Output format. `text` is default; `json` is for CI tooling.
- `--log-file <path>`: Write logs to a file in addition to stdout.
- `--no-color`: Disable ANSI colors.
- `--ci`: Enables CI-friendly behavior (non-interactive prompts, stable formatting).
- `--profile`: Emit per-step timings summary.

#### Container Options
- `--cold`: Always create a fresh container (ignore warm container).
- `--warm`: Reuse existing warm container if available (default behavior).
- `--image <image>`: Use a custom base Docker image instead of the default.

#### Step Selectors
Selectors match the step display name. Recommended naming conventions:
- `Dotnet.Restore`
- `Dotnet.Build`
- `Dotnet.Test`
- `Dotnet.Publish`
- `Ef.DatabaseUpdate`
- `Azure.WebAppDeploy`

Wildcard examples:
- `--only Dotnet.*`
- `--skip Azure.*`

#### Exit Codes
- `0`: Success
- `1`: Step failed
- `2`: Script compilation/config error
- `3`: Missing prerequisite (e.g., dotnet not found)
- `4`: User cancellation / prompt rejected (non-CI)
- `5`: Internal error

### 16.4 `ando list`

Lists discovered workflows and steps without executing them.

#### Usage
```bash
ando list [options]
```

#### Examples
```bash
ando list
ando list --format json
ando list ci
```

#### Output (text example)
```
Workflows:
  ci (default)

ci:
  1. Dotnet.Restore (WebApi)
  2. Dotnet.Build   (WebApi)
  3. Dotnet.Test    (WebApi)
  4. Dotnet.Publish (WebApi) -> artifacts/publish
  5. Ef.DatabaseUpdate (WebApi, default)
  6. Azure.WebAppDeploy (picturehive-prod, production slot)
```

### 16.5 `ando init`

Creates a starter `build.ando` (and optionally a `.ando/` directory) tailored to the repository.

#### Usage
```bash
ando init [options]
```

#### Options
- `--force`: Overwrite existing files
- `--template <minimal|ci|azure-webapp>`: Choose a template (default: `minimal`)
- `--project <path>`: Primary `.csproj` path to seed refs (optional; auto-detect if omitted)

#### Example
```bash
ando init --template azure-webapp --project ./src/WebApi/WebApi.csproj
```

### 16.6 `ando doctor`

Diagnoses prerequisites and common issues.

#### Usage
```bash
ando doctor [options]
```

Checks include:
- `dotnet` installed and version compatible
- `dotnet ef` available (or can be restored as a tool)
- Repository has a valid `build.ando`
- Azure tooling present when Azure steps are detected (`az` CLI, login state) (optional)
- Permissions and writeability for artifacts/temp directories

#### Output (text example)
```
✔ dotnet 8.0.2 found
✔ build.ando found
✔ artifacts directory writable
⚠ Azure CLI not found (required for Azure.WebAppDeploy)
✖ Missing env var SQL_CONNECTION (required)
```

### 16.7 `ando clean`

Removes ANDO-managed outputs and containers.

#### Usage
```bash
ando clean [options]
```

#### Options
- `--artifacts`: Remove `Context.Paths.Artifacts` (default: on)
- `--temp`: Remove `Context.Paths.Temp` (default: on)
- `--cache`: Remove `.ando/cache/` (NuGet and npm caches)
- `--container`: Remove the project's warm container
- `--all`: Remove artifacts, temp, cache, and container

### 16.8 `ando env`

Prints resolved runner context for debugging.

#### Usage
```bash
ando env [options]
```

#### Examples
```bash
ando env
ando env --format json
```

Includes:
- `ContextPaths` resolved locations
- `ContextVars` keys (optionally redacted)
- Selected environment variables (explicit allowlist only)

Options:
- `--show-vars`: include `ContextVars` contents
- `--redact`: redact values that look like secrets (default on when `--show-vars`)
- `--allow-env <NAME>`: include specific env var values in output (repeatable)

### 16.9 `ando version`

Prints version and runtime info.

#### Usage
```bash
ando version
```

Example output:
```
ando 0.3.0
runtime: .NET 8.0.2
os: linux-x64
script host: Roslyn C# scripting
```

### 16.10 Logging & Output Conventions

#### Text Output
- Each step is printed with a stable prefix and duration:
  - `▶ Dotnet.Build (WebApi) ...`
  - `✔ Dotnet.Build (WebApi) 00:07.412`
  - `✖ Ef.DatabaseUpdate (WebApi) 00:02.103`

#### JSON Output (CI)
When `--format json` is used, ANDO emits newline-delimited JSON events (NDJSON):
- `workflow_started`
- `step_started`
- `step_output` (optional, chunked)
- `step_finished`
- `workflow_finished`

#### Secret Masking
- Values sourced from `Context.Vars.EnvRequired` or marked secret are masked in logs (`****`)
- Masking is best-effort and must not be treated as a security boundary

### 16.11 Configuration Resolution

By default ANDO resolves:
- `build.ando` at repository root; if not found, searches upward from current directory
- `Context.Paths.Root` = directory containing `build.ando`
- `Context.Paths.Artifacts` = `<root>/artifacts`
- `Context.Paths.Temp` = `<root>/.ando/tmp`
- Container name = `ando-<project-directory-name>`

Future: support `.ando/config.toml` for overrides, without requiring it.
