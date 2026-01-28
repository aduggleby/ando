# ANDO CLI Architecture

## Overview

ANDO is a typed C# build system using Roslyn scripting. Build scripts (`build.csando` files) are real C# code executed via Microsoft.CodeAnalysis.CSharp.Scripting (Roslyn) in Docker containers for isolation and reproducibility.

## Core Architectural Principles

1. **Build scripts are real C# code** - Full IDE support, type checking, and IntelliSense
2. **Docker isolation by default** - All builds run in containers for reproducibility
3. **Lazy evaluation pattern** - Operations register steps during script execution; steps execute later via WorkflowRunner
4. **Executor abstraction** - Commands can run locally (ProcessRunner) or in containers (ContainerExecutor) with identical API

## High-Level Architecture

```
┌─────────────────────────────────────────┐
│  CLI Entry Point (AndoCli)              │
│  - Parse arguments                      │
│  - Manage build lifecycle               │
│  - Docker orchestration                 │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  Scripting Engine (ScriptHost + Roslyn) │
│  - Load build.csando                    │
│  - Compile & execute                    │
│  - Register steps                       │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  Build Context & Operations             │
│  - BuildContext (state container)       │
│  - ScriptGlobals (API surface)          │
│  - Operations (Dotnet, Npm, Azure, etc) │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  Step Registry & Workflow               │
│  - StepRegistry (collect steps)         │
│  - WorkflowRunner (execute sequentially)│
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  Execution Layer                        │
│  - ICommandExecutor (abstraction)       │
│  - ProcessRunner (local host)           │
│  - ContainerExecutor (docker exec)      │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  Docker & System Integration            │
│  - DockerManager (container lifecycle)  │
│  - Artifact copying                     │
│  - Logging (IBuildLogger)               │
└─────────────────────────────────────────┘
```

---

## Directory Structure

```
src/Ando/
├── Cli/              # CLI entry point and command routing
│   ├── Program.cs    # Bootstrap entry point
│   └── AndoCli.cs    # Main CLI handler
├── Context/          # Build context and path management
│   ├── BuildPath.cs          # Type-safe path manipulation
│   ├── BuildContextObject.cs # Unified context exposed to scripts
│   └── PathsContext.cs       # Standardized project paths
├── Execution/        # Command execution abstractions
│   ├── ICommandExecutor.cs   # Core execution interface
│   ├── CommandExecutorBase.cs# Shared execution logic
│   ├── ProcessRunner.cs      # Host machine execution
│   ├── ContainerExecutor.cs  # Docker container execution
│   └── DockerManager.cs      # Container lifecycle management
├── Logging/          # Logging infrastructure
│   ├── IBuildLogger.cs       # Logger interface hierarchy
│   └── ConsoleLogger.cs      # Production console logger
├── Operations/       # Build operations (Dotnet, Npm, Azure, etc.)
│   ├── OperationsBase.cs     # Base class with step registration
│   ├── DotnetOperations.cs   # .NET CLI operations
│   ├── EfOperations.cs       # Entity Framework operations
│   ├── NpmOperations.cs      # Node.js package manager
│   ├── AzureOperations.cs    # Azure CLI operations
│   └── ...                   # Many more operation types
├── References/       # Type-safe project references
│   ├── ProjectRef.cs         # .NET project reference
│   ├── DirectoryRef.cs       # Directory reference
│   ├── EfContextRef.cs       # EF DbContext reference
│   └── VersionRef.cs         # Version file reference
├── Scripting/        # Roslyn script integration
│   ├── ScriptHost.cs         # Roslyn script loader/executor
│   ├── BuildContext.cs       # Build state container
│   ├── ScriptGlobals.cs      # API surface for scripts
│   └── BuildOperations.cs    # Operation container
├── Steps/            # Build step management
│   ├── BuildStep.cs          # Single executable step
│   └── StepRegistry.cs       # Step collection
├── Workflow/         # Workflow execution
│   ├── WorkflowRunner.cs     # Sequential step executor
│   ├── WorkflowResult.cs     # Execution result
│   └── BuildOptions.cs       # Build configuration
└── Profiles/         # Profile system for conditional builds
    └── ProfileRegistry.cs    # Profile management
```

---

## Component Deep Dive

### 1. CLI Entry Point (Cli/)

#### Program.cs
Minimal bootstrap using C# top-level statements. Creates `AndoCli` instance and calls `RunAsync()`.

#### AndoCli.cs
The main CLI handler orchestrating the entire build lifecycle.

**Command Routing:**
- `ando` or `ando run` - Execute build
- `ando verify` - Check script for compilation errors
- `ando clean` - Remove artifacts/cache/containers
- `ando help` - Display usage information

**Key Responsibilities:**
1. **Build Script Discovery** - Looks for `build.csando` in current directory (or `-f` specified file)
2. **Profile Management** - Parses `-p/--profile` flags and activates profiles
3. **Docker Initialization** - Creates/reuses warm containers
4. **Environment Variable Loading** - Prompts to load `.env` files
5. **Build Execution** - Calls ScriptHost and WorkflowRunner
6. **Artifact Copying** - Copies outputs from container to host

**Exit Codes:**
| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Build failed |
| 2 | Script not found or file error |
| 3 | Docker not available |
| 4 | Profile validation failed |
| 5 | Generic error (exception) |

**CLI Flags:**
```
Build Options:
  -f, --file <file>          Use custom build file
  -p, --profile <name>       Activate profiles (comma-separated)
  --read-env                 Auto-load .env without prompting
  --verbosity <level>        Quiet, Minimal, Normal (default), Detailed
  --no-color                 Disable colored output
  --cold                     Force fresh container (skip warm container reuse)
  --image <image>            Override Docker image
  --dind                     Mount Docker socket for nested builds

Clean Options:
  --artifacts                Remove artifacts directory
  --temp                     Remove .ando/tmp
  --container                Remove project's warm container (clears package caches)
  --all                      Remove all of above
```

---

### 2. Context System (Context/)

#### BuildPath
Immutable value type for type-safe path manipulation.

```csharp
// Operators for intuitive path construction
var binDir = Root / "bin" / "Release";
var testResults = Temp / "test-results";

// Methods
path.Exists()      // Check if exists
path.IsDirectory() // Check if directory
path.IsFile()      // Check if file

// Implicit string conversion
File.Exists(binDir)  // Works due to implicit conversion
```

**Design Decisions:**
- `readonly struct` for zero-allocation pass-by-value semantics
- Operator `/` follows Cake/FAKE conventions for readability
- Always normalizes to absolute paths to avoid working directory ambiguity

#### PathsContext
Provides standardized project paths:
- `Root` - Project root directory (where build.csando is)
- `Temp` - Temporary files (`root/.ando/tmp`)

#### BuildContextObject
Unified context object exposed as global variable in scripts. Aggregates path information.

---

### 3. Execution System (Execution/)

#### ICommandExecutor Interface
Core abstraction enabling command execution in different environments:

```csharp
public interface ICommandExecutor
{
    Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options);
    bool IsAvailable(string command);
}
```

**CommandOptions:**
- `WorkingDirectory` - Directory to run command in
- `Environment` - Environment variables to set
- `TimeoutMs` - Command timeout (default 5 minutes, -1 for no timeout)
- `Interactive` - Whether process inherits console streams

**CommandResult:**
```csharp
record CommandResult(
    int ExitCode,
    bool Success,
    string? Error = null,
    string? Output = null
);
```

#### ProcessRunner
Executes commands on the host machine. Used for:
- Docker CLI commands (docker run, docker exec, docker cp)
- Commands that must run on host to manage containers

**Implementation Details:**
- Uses `ProcessStartInfo.ArgumentList` for safe argument escaping
- Streams output in real-time for responsive feedback
- Treats stderr as regular output (many tools use it for progress)
- Kills entire process tree on timeout

#### ContainerExecutor
Executes commands inside a Docker container via `docker exec`.

**Key Features:**
- Wraps commands in `docker exec` calls
- Translates host paths to container paths (`/workspace` mount point)
- Preserves environment variables and working directory
- Checks tool availability using `which` command in container

**Path Translation:**
Container workspace is mounted at `/workspace`. ContainerExecutor translates:
- Relative paths → relative to `/workspace`
- Host absolute paths → translate to `/workspace` relative paths
- Container paths starting with `/workspace` → kept as-is

#### CommandExecutorBase
Shared base class for command executors with process execution logic.

**Execution Modes:**
1. **Interactive Mode** - Process inherits console streams (stdin/stdout/stderr)
   - Used for child builds that may prompt for input
   - Output not captured

2. **Redirection Mode** - Streams are redirected and captured
   - Output streamed to logger in real-time
   - Used for most build commands
   - TaskCompletionSource ensures all output captured before return

**Timeout Handling:**
- Default: 5 minutes
- Set to -1 for no timeout
- Kills entire process tree if exceeded

---

### 4. Docker Integration (Execution/DockerManager.cs)

Manages Docker container lifecycle for isolated builds.

#### Container Configuration
```csharp
var containerConfig = new ContainerConfig
{
    Name = "ando-project-name-xxxx",  // Name includes MD5 hash of build script
    ProjectRoot = "/path/to/project",  // Host path for volume mounts
    LocalProjectRoot = null,           // Path inside current container (DinD only)
    Image = "ubuntu:22.04",            // Docker image
    MountDockerSocket = false          // For Docker-in-Docker
};
```

#### Warm Container Pattern
1. **First build** - Creates new container, copies project files, runs build
2. **Subsequent builds** - Reuses running container, re-copies project files for fresh state
3. **Cache persistence** - NuGet and npm caches persist inside the warm container

#### Project File Isolation
- Project files are **copied into** container (not mounted) for true isolation
- Docker operations cannot modify host files during build
- On warm container reuse, files are re-copied to ensure fresh build state
- No host directories are created for caches (caches live inside container only)

#### Volume Mounts
```dockerfile
-v /var/run/docker.sock:/var/run/docker.sock  # Optional for Docker-in-Docker
```

#### Excluded Directories (Not Copied to Container)
`.git`, `node_modules`, `bin`, `obj`, `.vs`, `.idea`, `packages`, `TestResults`, `coverage`, `.pytest_cache`, `__pycache__`, `dist`, `build`, `target`

#### Container Start Command
```bash
docker run -d --name ${container_name} \
  -v ... \
  ${image} \
  tail -f /dev/null
```
Keeps container running indefinitely for "warm container" reuse.

#### DinD (Docker-in-Docker) Support
- When running Ando inside a Docker container building Docker images
- Mounts Docker socket: `--dind` flag
- `ANDO_HOST_ROOT` env var can override host path for nested containers
- ProjectRoot = host path (for Docker mounts), LocalProjectRoot = container path (for tar operations)

---

### 5. Logging System (Logging/)

#### IBuildLogger Interface Hierarchy
Three-part interface segregation following Interface Segregation Principle:

**IMessageLogger** - Basic message logging:
```csharp
void Info(string message);      // Normal+ verbosity
void Warning(string message);   // Minimal+ verbosity
void Error(string message);     // Always shown
void Debug(string message);     // Detailed verbosity only
```

**IStepLogger** - Build step lifecycle events:
```csharp
void StepStarted(string stepName, string? context);
void StepCompleted(string stepName, TimeSpan duration, string? context);
void StepFailed(string stepName, TimeSpan duration, string? message);
void StepSkipped(string stepName, string? reason);
void LogStep(string level, string message);  // Single-line log steps
```

**IWorkflowLogger** - Workflow lifecycle events:
```csharp
void WorkflowStarted(string workflowName, string? scriptPath, int totalSteps);
void WorkflowCompleted(string workflowName, string? scriptPath, TimeSpan duration, int stepsRun, int stepsFailed);
```

**IBuildLogger** - Combined interface extending all three.

#### ConsoleLogger
Production implementation with:
- **Verbosity levels** - Quiet, Minimal, Normal, Detailed
- **Color support** - Respects `--no-color` flag and `NO_COLOR` env var
- **File logging** - Writes to `build.csando.log` in project directory
- **Indentation** - Tracks nesting level for nested builds
- **Progress formatting** - Shows step numbers, durations, success/failure

**Output Format:**
```
▶ [1/5] Dotnet.Build (MyApp) ...
  Command output streamed here
✓ [1/5] Dotnet.Build (MyApp) completed in 3.2s

▶ [2/5] Dotnet.Test (MyApp) ...
✗ [2/5] Dotnet.Test (MyApp) failed in 1.1s
```

---

### 6. Scripting System (Scripting/)

#### ScriptHost
Roslyn-based script host for loading and executing build scripts.

**LoadScriptAsync Flow:**
1. **Create BuildContext** - Container for build state
2. **Set Active Profiles** - Profiles from CLI
3. **Create ScriptGlobals** - API exposed to script
4. **Configure Roslyn** - Load assemblies and namespaces
5. **Execute Script** - Run C# code (registers steps, doesn't execute them yet)
6. **Return Context** - Ready for WorkflowRunner

**Roslyn Configuration:**
```csharp
var options = ScriptOptions.Default
    .WithReferences(
        typeof(BuildContext).Assembly,    // ANDO types
        typeof(object).Assembly,          // mscorlib
        typeof(Console).Assembly,         // System.Console
        typeof(File).Assembly,            // System.IO
        typeof(Task).Assembly,            // System.Threading.Tasks
        typeof(Enumerable).Assembly       // System.Linq
    )
    .WithImports(
        "System",
        "System.IO",
        "System.Linq",
        "System.Threading.Tasks",
        "System.Collections.Generic",
        "Ando.Context",
        "Ando.Profiles",
        "Ando.References",
        "Ando.Operations",
        "Ando.Workflow",
        "Ando.Steps"
    );
```

**Script Compilation vs Execution:**
- **Compilation check** - `VerifyScriptAsync()` compiles without executing
- **Full execution** - `LoadScriptAsync()` compiles and executes

#### BuildContext
Container for all build state and operations:

```csharp
public class BuildContext
{
    public BuildContextObject Context { get; }        // Paths
    public BuildOptions Options { get; }              // Container config
    public BuildOperations Operations { get; }        // All operation instances
    public StepRegistry StepRegistry { get; }         // Collected steps
    public ProfileRegistry ProfileRegistry { get; }   // Active profiles
    public IBuildLogger Logger { get; }               // Logger
    public ICommandExecutor Executor { get; set; }    // Mutable executor
}
```

**Key Design:**
- Single object passed through entire build lifecycle
- `Executor` is mutable - switched from ProcessRunner to ContainerExecutor after script load
- `Operations` receives executor factory lambda so they always get current executor

#### ScriptGlobals
Global variables exposed to build scripts (API surface):

```csharp
// Path accessors
public BuildPath Root { get; }              // Root directory
public BuildPath Temp { get; }              // Temp directory

// Operations
public DotnetOperations Dotnet { get; }
public EfOperations Ef { get; }
public NpmOperations Npm { get; }
public AzureOperations Azure { get; }
public BicepOperations Bicep { get; }
// ... more operations

// Helper methods
public DirectoryRef Directory(string path = ".")
public Profile DefineProfile(string name)
public string? Env(string name, bool required = true)
```

**Example usage in build.csando:**
```csharp
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");
var frontend = Directory("./frontend");

Dotnet.Restore(project);
Dotnet.Build(project);
Npm.Ci(frontend);
Npm.Run(frontend, "build");
```

#### BuildOperations
Container for all build operation instances:

```csharp
public class BuildOperations
{
    public DotnetOperations Dotnet { get; }
    public EfOperations Ef { get; }
    public NpmOperations Npm { get; }
    public AzureOperations Azure { get; }
    public BicepOperations Bicep { get; }
    public CloudflareOperations Cloudflare { get; }
    public FunctionsOperations Functions { get; }
    public AppServiceOperations AppService { get; }
    public ArtifactOperations Artifacts { get; }
    public NodeInstallOperations Node { get; }
    public LogOperations Log { get; }
    public NugetOperations Nuget { get; }
    public AndoOperations Ando { get; }
    public GitOperations Git { get; }
    public GitHubOperations GitHub { get; }
    public DockerOperations Docker { get; }
    // ... more
}
```

All operations receive:
- `StepRegistry` - for registering steps
- `IBuildLogger` - for logging
- `Func<ICommandExecutor>` - factory for getting current executor
- Operation-specific ensurers (e.g., DotnetSdkEnsurer)

---

### 7. Operations System (Operations/)

#### OperationsBase
Base class with shared step registration logic:

```csharp
protected void RegisterCommand(
    string stepName,
    string command,
    string[] args,
    string? context = null,
    string? workingDirectory = null,
    Dictionary<string, string>? environment = null);

protected void RegisterCommand(
    string stepName,
    string command,
    Func<ArgumentBuilder> buildArgs,  // Lazy evaluation
    string? context = null,
    string? workingDirectory = null,
    Dictionary<string, string>? environment = null);

protected void RegisterCommandWithEnsurer(
    string stepName,
    string command,
    string[] args,
    Func<Task>? ensurer,             // Auto-install SDKs
    string? context = null,
    // ...
);
```

**Key Pattern:**
- Operations don't execute commands - they register steps
- At execution time, executor factory is called to get current executor
- Ensurer (if provided) runs at execution time for auto-install

#### DotnetOperations
.NET CLI operations:

```csharp
var project = Dotnet.Project("./src/MyApp/MyApp.csproj");

Dotnet.Restore(project, o => o.WithNoCache());
Dotnet.Build(project, o => o.WithConfiguration(Configuration.Release));
Dotnet.Test(project, o => o.WithConfiguration(Configuration.Debug));
Dotnet.Publish(project, o => o.WithConfiguration(Configuration.Release).WithRuntime("linux-x64"));
Dotnet.Pack(project);
Dotnet.SdkInstall("9.0");  // Auto-install specific version
```

**Options Pattern:**
All operations use fluent configuration via optional `Action<Options>` parameter:
```csharp
Dotnet.Build(project, o =>
{
    o.Configuration = Configuration.Release;
    o.NoRestore = true;
    o.Runtime = "linux-x64";
});
```

#### Available Operations

| Operation | Purpose |
|-----------|---------|
| `DotnetOperations` | .NET CLI (restore, build, test, publish, pack) |
| `EfOperations` | Entity Framework (migrations, database updates) |
| `NpmOperations` | Node.js package manager |
| `AzureOperations` | Azure CLI authentication and management |
| `BicepOperations` | ARM template deployments |
| `FunctionsOperations` | Azure Functions deployment |
| `AppServiceOperations` | App Service deployment |
| `CloudflareOperations` | Cloudflare Pages deployment |
| `ArtifactOperations` | Register files to copy from container to host |
| `GitOperations` | Git tagging and pushing |
| `GitHubOperations` | GitHub PRs, releases, container registry |
| `DockerOperations` | Docker image building |
| `NugetOperations` | NuGet package packing and pushing |
| `LogOperations` | Script logging output |
| `NodeInstallOperations` | Node.js global installation |
| `AndoOperations` | Nested build execution |

---

### 8. Steps System (Steps/)

#### BuildStep
Represents a single executable step:

```csharp
public class BuildStep(
    string Name,                    // e.g., "Dotnet.Build"
    Func<Task<bool>> Execute,       // Async function
    string? Context = null          // e.g., project name
)
{
    public string DisplayName => Context != null ? $"{Name} ({Context})" : Name;
    public bool IsLogStep { get; init; }          // Special log-only step
    public LogStepLevel LogLevel { get; init; }   // For log steps
    public string? LogMessage { get; init; }      // For log steps
}
```

**Design:**
- Name identifies step type
- Context provides additional identification (e.g., project name) for logging
- Execute returns `Task<bool>` to indicate success/failure
- IsLogStep flag for special single-line output steps
- Immutable after construction

#### StepRegistry
Collects steps registered during script execution:

```csharp
public interface IStepRegistry
{
    IReadOnlyList<BuildStep> Steps { get; }
    void Register(BuildStep step);
    void Register(string name, Func<Task<bool>> execute, string? context = null);
    void Clear();
}
```

**Design:**
- Maintains insertion order for predictable execution
- IReadOnlyList prevents external modification
- Interface allows mocking in tests

---

### 9. Workflow System (Workflow/)

#### WorkflowRunner
Executes all registered steps sequentially.

**Execution Flow:**
1. **Log workflow start** - Shows total step count
2. **Execute steps sequentially**
   - Handle log steps (special single-line output)
   - Execute regular steps
   - On failure, log error and stop (fail-fast)
3. **Log workflow completion** - Show summary and timing

**Fail-Fast Behavior:**
- Stops on first step failure (doesn't continue with remaining steps)
- Preserves build state at failure point for debugging

**Example Output:**
```
▶ Build: build.csando [5 steps]
  ▶ [1/5] Dotnet.Restore (MyApp)
    Restoring...
  ✓ [1/5] Dotnet.Restore (MyApp) completed in 0.5s

  ▶ [2/5] Dotnet.Build (MyApp)
    Building...
  ✓ [2/5] Dotnet.Build (MyApp) completed in 3.2s

  ▶ [3/5] Dotnet.Test (MyApp)
    Running tests...
  ✗ [3/5] Dotnet.Test (MyApp) failed in 1.1s

✗ Build: build.csando failed (1 of 3 steps failed) in 4.8s
```

#### WorkflowResult
Result of workflow execution:

```csharp
public class WorkflowResult
{
    public string WorkflowName { get; init; }           // "build"
    public bool Success { get; init; }                  // Success status
    public TimeSpan Duration { get; init; }             // Total time
    public List<StepResult> StepResults { get; init; }  // Individual results
    public int StepsRun => StepResults.Count;
    public int StepsFailed => StepResults.Count(s => !s.Success);
}

public class StepResult
{
    public string StepName { get; init; }
    public string? Context { get; init; }
    public bool Success { get; init; }
    public TimeSpan Duration { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
}
```

#### BuildOptions
Internal build configuration:

```csharp
public class BuildOptions
{
    public string? Image { get; private set; }  // Docker image
    internal BuildOptions UseImage(string image) { ... }
}
```

Set via script: `Ando.UseImage("custom:latest")`

---

### 10. References System (References/)

#### ProjectRef
Type-safe project reference:

```csharp
var project = ProjectRef.From("./src/MyApp/MyApp.csproj");

// Properties:
project.Path        // "./src/MyApp/MyApp.csproj"
project.Name        // "MyApp" (extracted from filename)
project.Directory   // "./src/MyApp" (directory containing project)

// Implicit string conversion allows use in APIs expecting strings
File.Exists(project)  // Works!
```

**Design:**
- Factory method `From()` for discoverability
- Private constructor ensures validation
- Implicit string conversion for seamless API use

#### Other References
- `DirectoryRef` - Similar to ProjectRef but for directories
- `EfContextRef` - Reference to Entity Framework DbContext type for migrations
- `VersionRef` - Reference to version files (for version bumping)

---

### 11. Profiles System (Profiles/)

#### ProfileRegistry
Tracks active and defined profiles:

```csharp
var release = DefineProfile("release");
var push = DefineProfile("push");

if (release)  // Implicit bool conversion
{
    Git.Tag(version);
}

if (push)
{
    Git.Push();
}
```

Activate via CLI: `ando -p release,push`

**Validation:**
- Profiles requested via CLI are validated to exist in script
- Prevents typos in profile names

---

## Data Flow: Complete Build Execution

### Step 1: CLI Startup
```
Program.cs
  └─> new AndoCli(args)
       ├─> Create ConsoleLogger
       └─> RunAsync()
```

### Step 2: Build Discovery & Validation
```
AndoCli.RunCommandAsync()
  ├─> FindBuildScript()  // Find build.csando or -f specified file
  ├─> PromptToLoadEnvFileAsync()  // Load .env if needed
  └─> Check Docker availability
```

### Step 3: Script Loading & Compilation
```
ScriptHost.LoadScriptAsync(scriptPath, containerRootPath="/workspace")
  ├─> Create BuildContext(rootPath)
  │   ├─> Create StepRegistry
  │   ├─> Create BuildOperations  // All operation instances
  │   │   ├─> DotnetOperations
  │   │   ├─> EfOperations
  │   │   ├─> NpmOperations
  │   │   ├─> AzureOperations
  │   │   └─> ... more operations
  │   └─> Set Executor = ProcessRunner (local for now)
  ├─> Create ScriptGlobals (API surface)
  │   ├─> Expose Root, Temp
  │   ├─> Expose all operations
  │   └─> Expose Directory(), Env(), DefineProfile()
  ├─> Configure Roslyn (assemblies, namespaces)
  └─> Execute script via CSharpScript.RunAsync()
       └─> Script code runs, calling operations like:
           Dotnet.Restore(project)  // Registers step, doesn't execute
           Dotnet.Build(project)     // Registers step, doesn't execute
           (Steps are added to StepRegistry)
```

### Step 4: Container Setup
```
AndoCli.RunCommandAsync() (continuation)
  ├─> Create DockerManager
  ├─> DockerManager.EnsureContainerAsync()
  │   ├─> Check for existing warm container
  │   ├─> If exists: start + re-copy project files
  │   ├─> If new: create container + copy project files
  │   │   └─> Use tar for efficient copying (excludes .git, node_modules, etc)
  │   └─> Return ContainerInfo
  └─> Switch executor in context
      └─> context.SetExecutor(new ContainerExecutor(containerId))
          └─> All operations now use container executor
```

### Step 5: Artifact Preparation
```
DockerManager.CleanArtifactsAsync()
  └─> docker exec clean /workspace/artifacts && mkdir -p /workspace/artifacts
```

### Step 6: Build Execution
```
WorkflowRunner.RunAsync(context.StepRegistry, context.Options)
  ├─> Log workflow start
  └─> For each step in registry:
       ├─> Log "Step started"
       ├─> Call step.Execute()  // Returns Task<bool>
       │   └─> At this point:
       │       ├─> ExecutorFactory() gets ContainerExecutor
       │       ├─> Command gets wrapped in: docker exec -w /workspace containerId command args
       │       ├─> ProcessRunner runs docker command
       │       ├─> Output streamed to logger in real-time
       │       └─> Return success status
       ├─> On success: Log "Step completed"
       ├─> On failure: Log "Step failed" and stop (fail-fast)
       └─> Save StepResult
  └─> Log workflow completion with summary
```

### Step 7: Artifact Copying
```
BuildContext.CopyArtifactsToHostAsync()
  ├─> For each registered artifact:
  │   └─> docker cp containerId:/workspace/path hostPath
  └─> For each registered zipped artifact:
       ├─> docker exec tar -czf /tmp/archive.tar.gz -C /workspace path
       ├─> docker cp containerId:/tmp/archive.tar.gz hostPath.tar.gz
       ├─> Extract on host
       ├─> Fix ownership (chown) if on Unix
       └─> Clean up container temp file
```

### Step 8: Return Exit Code
```
AndoCli.RunAsync()
  └─> Return 0 (success) or 1 (failure)
       └─> Shell receives exit code
```

---

## Error Handling Strategy

### Error Propagation
| Error Type | Handling | Exit Code |
|------------|----------|-----------|
| Script Compilation Errors | Caught in ScriptHost, logged | 5 |
| Docker Not Available | Checked early, helpful installation instructions | 3 |
| Build Step Failures | Logged, fail-fast stops execution | 1 |
| Missing Environment Variables | Caught in Env() call, throws InvalidOperationException | 5 |
| Profile Validation Errors | Caught after script load | 4 |

### Helpful Error Messages
- Docker installation instructions by OS
- "Required environment variable not set" with variable name
- Step failure shows tool installation instructions
- Roslyn compilation errors reported with full diagnostic info

### Logging Levels
| Level | Shown When |
|-------|------------|
| Error | Always |
| Warning | Minimal+ verbosity |
| Info | Normal+ verbosity |
| Debug | Detailed verbosity only |

---

## Configuration

### .env File Support
Located in project root. Format:
```
KEY=VALUE
KEY="quoted value"
KEY='single quoted'
# Comments ignored
```

**Auto-loading:**
- Prompt user on first build (Y/n/always)
- `--read-env` flag skips prompt for this and sub-builds
- `ANDO_AUTO_LOAD_ENV=1` enables for sub-builds

---

## Key Design Patterns

### 1. Step Registration Pattern
Operations don't execute commands directly. Instead, they register steps in StepRegistry during script execution. WorkflowRunner executes all registered steps later in sequence.

**Benefit:** Enables logging, error handling, timing, and potentially parallelism all in one place.

### 2. Executor Strategy Pattern
ICommandExecutor abstraction allows seamless switching between ProcessRunner (local) and ContainerExecutor (Docker) without changing operation logic.

**Benefit:** Single codebase works in both local and containerized environments.

### 3. Executor Factory Pattern
Operations receive `Func<ICommandExecutor>` factory instead of direct executor reference. Allows executor to be swapped after script loading without affecting operations.

**Benefit:** Script loading can use local ProcessRunner, then switch to ContainerExecutor for execution.

### 4. Options Builder Pattern
All operations use `Action<Options>?` parameter for fluent configuration:
```csharp
Dotnet.Build(project, o =>
{
    o.Configuration = Configuration.Release;
    o.NoRestore = true;
});
```

**Benefit:** More discoverable than multiple overloads, clean API.

### 5. Lazy Evaluation
- Script execution registers steps
- Steps executed later allows path translation (container paths vs host paths)
- Option builders called at execution time, not registration time

**Benefit:** Supports complex scenarios like nested builds with different executors.

### 6. Warm Containers
Containers persist between builds, with project files re-copied for fresh state while caches persist.

**Benefit:** Faster builds (no container creation overhead) with reproducibility (fresh source each time).

### 7. Immutable Values
BuildPath, BuildStep, ContainerInfo, CommandResult use readonly struct/record pattern.

**Benefit:** Thread safety, prevents accidental mutations.

### 8. Interface Segregation
IMessageLogger, IStepLogger, IWorkflowLogger separate into IBuildLogger.

**Benefit:** Components depend only on logging they need.

---

## Extension Points

### Adding New Operations
1. Create new `*Operations` class inheriting `OperationsBase`
2. Implement methods that call `RegisterCommand()` or `RegisterCommandWithEnsurer()`
3. Add to `BuildOperations` constructor
4. Expose in `ScriptGlobals`

### Custom Executors
1. Implement `ICommandExecutor` interface
2. Override `PrepareProcessStartInfo()` and `IsAvailable()`
3. Set via `context.SetExecutor()`

### Custom Loggers
1. Implement `IBuildLogger` interface
2. Pass to AndoCli or BuildContext

---

## Testing

The CLI is tested via:
- **Unit tests** - Individual component testing with mocks
- **Integration tests** - Docker-based execution testing
- **E2E tests** - Full build script execution

Key test doubles:
- `MockExecutor` - ICommandExecutor that records commands without executing
- `TestLogger` - IBuildLogger that captures events for assertions

---

## Comparison to Related Tools

| Feature | ANDO | Cake | Nuke | Make/Bash |
|---------|------|------|------|-----------|
| Language | Pure C# | Build DSL | Pure C# | Shell |
| Type Safety | Full | Partial | Full | None |
| IDE Support | Full IntelliSense | Limited | Good | Variable |
| Docker Isolation | Default | Manual | Manual | Manual |
| Learning Curve | Low (C# devs) | Medium | Medium | Low |
| Ecosystem | New | Mature | Growing | Universal |

---

## Summary

ANDO's CLI architecture emphasizes:

1. **Type Safety** - Real C# code with IntelliSense
2. **Simplicity** - Minimal API surface, easy to learn
3. **Reproducibility** - Docker isolation by default
4. **Performance** - Warm containers, dependency caching
5. **Extensibility** - Clear extension points for custom operations
6. **Maintainability** - Well-documented code, design patterns, layered architecture

The system flows from CLI → Script Compilation → Step Registration → Workflow Execution → Artifact Copying, with Docker integration at every step providing isolation and consistency.
