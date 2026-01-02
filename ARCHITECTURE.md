# ANDO Architecture

This document describes the internal architecture of ANDO, how components interact, and how to extend the system with new operations.

## Overview

ANDO follows a pipeline architecture with Docker-based isolated execution:

```
build.ando → ScriptHost → BuildContext → StepRegistry → DockerManager → ContainerExecutor → Results
           (compile)    (globals)      (steps)        (container)     (execute)
```

1. **ScriptHost** compiles `build.ando` using Roslyn C# Scripting
2. **ScriptGlobals** exposes the API (`Dotnet`, `Ef`, `Npm`, `Project`, etc.) to the script
3. **Operations** register **BuildSteps** into the **StepRegistry**
4. **DockerManager** creates/manages the build container
5. **ContainerExecutor** runs commands inside the container via `docker exec`
6. **WorkflowRunner** executes steps sequentially and collects results
7. **IBuildLogger** provides structured output throughout

## Component Diagram

```
┌─────────────────────────────────────────────────────────────────────┐
│                              CLI Layer                               │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                         AndoCli                                │  │
│  │  - Parses arguments                                           │  │
│  │  - Finds build.ando                                           │  │
│  │  - Checks Docker availability                                 │  │
│  │  - Manages container lifecycle                                │  │
│  │  - Orchestrates execution                                     │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                           Scripting Layer                            │
│  ┌─────────────────────┐    ┌─────────────────────────────────────┐ │
│  │     ScriptHost      │───▶│           ScriptGlobals             │ │
│  │                     │    │                                     │ │
│  │  - Loads build.ando │    │  - Context (paths & vars)           │ │
│  │  - Roslyn compile   │    │  - Dotnet (operations)              │ │
│  │  - Injects globals  │    │  - Ef (operations)                  │ │
│  └─────────────────────┘    │  - Npm (operations)                 │ │
│                             │  - Project (helper)                 │ │
│                             │  - Workflow() method                │ │
│                             └─────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                           Core Layer                                 │
│  ┌───────────────┐  ┌───────────────┐  ┌─────────────────────────┐  │
│  │  BuildContext │  │ StepRegistry  │  │    WorkflowRunner       │  │
│  │               │  │               │  │                         │  │
│  │  - Context    │  │  - Steps[]    │  │  - Sequential execute   │  │
│  │    - Paths    │  │  - Register() │  │  - Fail-fast            │  │
│  │    - Vars     │  │  - Clear()    │  │  - Timing               │  │
│  │  - Executor   │  │               │  │  - Result collection    │  │
│  │  - Workflows  │  │               │  │                         │  │
│  └───────────────┘  └───────────────┘  └─────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                        Execution Layer                               │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │  DockerManager  │  │ContainerExecutor│  │   ProcessRunner     │  │
│  │                 │  │                 │  │                     │  │
│  │  - Create       │  │  - docker exec  │  │  - Local execution  │  │
│  │  - Start/Stop   │  │  - Real-time    │  │  - Fallback mode    │  │
│  │  - Warm reuse   │  │    streaming    │  │                     │  │
│  │  - Clean        │  │  - Exit codes   │  │                     │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         Operations Layer                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │ DotnetOperations│  │  EfOperations   │  │   NpmOperations     │  │
│  │                 │  │                 │  │                     │  │
│  │  - Restore()    │  │  - DbContext()  │  │  - Install()        │  │
│  │  - Build()      │  │  - Update()     │  │  - Ci()             │  │
│  │  - Test()       │  │  - Migrate()    │  │  - Run()            │  │
│  │  - Publish()    │  │  - Script()     │  │  - Test()           │  │
│  │  - Tool()       │  │                 │  │                     │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
                                   │
                                   ▼
┌─────────────────────────────────────────────────────────────────────┐
│                         Support Layer                                │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐  │
│  │   References    │  │    Context      │  │     Logging         │  │
│  │                 │  │                 │  │                     │  │
│  │  - ProjectRef   │  │  - BuildContext │  │  - IBuildLogger     │  │
│  │  - EfContextRef │  │    Object       │  │  - ConsoleLogger    │  │
│  │                 │  │  - PathsContext │  │                     │  │
│  │                 │  │  - VarsContext  │  │                     │  │
│  │                 │  │  - BuildPath    │  │                     │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

## Key Components

### 1. ScriptHost (`Scripting/ScriptHost.cs`)

Responsible for compiling and executing `build.ando` files using Roslyn C# Scripting.

```csharp
public class ScriptHost
{
    public async Task<BuildContext> LoadScriptAsync(string scriptPath, string rootPath)
    {
        var context = new BuildContext(rootPath, _logger);
        var globals = new ScriptGlobals(context);

        var options = ScriptOptions.Default
            .WithReferences(typeof(BuildContext).Assembly, ...)
            .WithImports("System", "Ando.Context", ...);

        await CSharpScript.RunAsync(scriptContent, options, globals, typeof(ScriptGlobals));

        return context;
    }
}
```

**Key Points:**
- Creates a `BuildContext` to hold state
- Wraps it in `ScriptGlobals` to expose the clean API
- Injects necessary references and imports
- Returns the populated context after script execution

### 2. ScriptGlobals (`Scripting/ScriptGlobals.cs`)

The API surface exposed to `build.ando` scripts. Properties on this class become global variables in the script.

```csharp
public class ScriptGlobals
{
    public BuildContextObject Context { get; }    // Context.Paths, Context.Vars
    public DotnetOperations Dotnet { get; }       // Dotnet.Build(), Dotnet.Test()
    public EfOperations Ef { get; }               // Ef.DatabaseUpdate()
    public NpmOperations Npm { get; }             // Npm.Install(), Npm.Run()
    public ProjectHelper Project { get; }         // Project.From("path")

    public void Workflow(string name, Action<WorkflowConfig>? configure = null)
    {
        _buildContext.Workflow(name, configure);
    }
}
```

### 3. Unified Context Object (`Context/BuildContextObject.cs`)

Provides access to paths and variables in a clean, unified way:

```csharp
public class BuildContextObject
{
    public PathsContext Paths { get; }   // Paths.Root, Paths.Artifacts, etc.
    public VarsContext Vars { get; }     // Vars["key"], Vars.Env("NAME")

    public BuildContextObject(string rootPath)
    {
        Paths = new PathsContext(rootPath);
        Vars = new VarsContext();
    }
}
```

**Usage in build.ando:**
```csharp
Context.Vars["environment"] = Context.Vars.Env("ASPNETCORE_ENVIRONMENT") ?? "Production";
var output = Context.Paths.Artifacts / "publish";
```

### 4. Execution Infrastructure

#### ICommandExecutor (`Execution/ICommandExecutor.cs`)

Interface for executing CLI commands, allowing swapping between local and container execution:

```csharp
public interface ICommandExecutor
{
    Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null);
    bool IsAvailable(string command);
}
```

#### ContainerExecutor (`Execution/ContainerExecutor.cs`)

Executes commands inside a Docker container via `docker exec`:

```csharp
public class ContainerExecutor : ICommandExecutor
{
    private readonly string _containerId;

    public async Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null)
    {
        var dockerArgs = new List<string> { "exec", "-w", workDir, _containerId, command };
        dockerArgs.AddRange(args);

        // Execute docker exec with real-time output streaming
        // ...
    }
}
```

**Key Features:**
- Real-time stdout/stderr streaming
- Working directory management inside container
- Environment variable injection
- Exit code handling

#### DockerManager (`Execution/DockerManager.cs`)

Manages Docker container lifecycle:

```csharp
public class DockerManager
{
    public bool IsDockerAvailable();
    public Task<ContainerInfo?> FindWarmContainerAsync(string containerName);
    public Task<ContainerInfo> EnsureContainerAsync(ContainerConfig config);
    public Task StopContainerAsync(string containerId);
    public Task RemoveContainerAsync(string containerName);
    public Task CleanArtifactsAsync(string containerId);
}
```

**Container Lifecycle:**
```
1. Check for warm container
2. Create new if needed (or start stopped container)
3. Clean artifacts directory
4. Execute workflow steps
5. Keep container warm for reuse
```

### 5. StepRegistry (`Steps/StepRegistry.cs`)

A simple collection that holds registered build steps in order.

```csharp
public class StepRegistry
{
    private readonly List<BuildStep> _steps = new();

    public IReadOnlyList<BuildStep> Steps => _steps;

    public void Register(string name, Func<Task<bool>> execute, string? context = null)
    {
        _steps.Add(new BuildStep(name, execute, context));
    }
}
```

### 6. Operations (e.g., `Operations/DotnetOperations.cs`)

Operations are classes that provide methods which register steps into the registry. They use the executor factory to run actual commands.

```csharp
public class DotnetOperations
{
    private readonly StepRegistry _registry;
    private readonly IBuildLogger _logger;
    private readonly Func<ICommandExecutor> _executorFactory;

    public void Build(ProjectRef project, Action<DotnetBuildOptions>? configure = null)
    {
        var options = new DotnetBuildOptions();
        configure?.Invoke(options);

        _registry.Register("Dotnet.Build", async () =>
        {
            var args = new List<string> { "build", project.Path };

            if (options.Configuration != null)
                args.AddRange(new[] { "-c", options.Configuration.ToString()! });

            var result = await _executorFactory().ExecuteAsync("dotnet", args.ToArray());
            return result.Success;
        }, project.Name);
    }
}
```

## Data Flow

### Script Execution Flow

```
1. CLI starts
   │
   ▼
2. Find build.ando (search upward from cwd)
   │
   ▼
3. Check Docker availability
   │
   ├─▶ Not available: Show installation instructions, exit 3
   │
   ▼
4. ScriptHost.LoadScriptAsync()
   │
   ├─▶ Create BuildContext (context, registry)
   ├─▶ Create ScriptGlobals (exposes API)
   ├─▶ Compile script with Roslyn
   └─▶ Execute script (populates registry)
   │
   ▼
5. DockerManager.EnsureContainerAsync()
   │
   ├─▶ Find or create container
   ├─▶ Mount project directory to /workspace
   ├─▶ Mount cache directories
   └─▶ Clean artifacts directory
   │
   ▼
6. Create ContainerExecutor for container
   │
   ▼
7. WorkflowRunner.RunAsync()
   │
   ├─▶ For each step in registry:
   │     ├─▶ Log step started
   │     ├─▶ Execute step (via ContainerExecutor)
   │     ├─▶ Stream output in real-time
   │     ├─▶ Log step completed/failed
   │     └─▶ Break on failure (fail-fast)
   │
   └─▶ Return WorkflowResult
   │
   ▼
8. CLI exits with appropriate code
```

### Container Architecture

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
│  │  │  Base: .NET SDK (Alpine/Debian)                     │  │  │
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

## Adding New Operations

### Step 1: Create a Reference Type (if needed)

If your operation targets a specific resource, create a typed reference:

```csharp
// References/DockerImageRef.cs
namespace Ando.References;

public class DockerImageRef
{
    public string Name { get; }
    public string Tag { get; }

    internal DockerImageRef(string name, string tag = "latest")
    {
        Name = name;
        Tag = tag;
    }

    public string FullName => $"{Name}:{Tag}";

    public override string ToString() => FullName;
}
```

### Step 2: Create Options Classes (if needed)

For operations with configuration:

```csharp
// Operations/DockerBuildOptions.cs
namespace Ando.Operations;

public class DockerBuildOptions
{
    public string? Dockerfile { get; private set; }
    public string? BuildContext { get; private set; }
    public List<string> BuildArgs { get; } = new();

    public DockerBuildOptions WithDockerfile(string path)
    {
        Dockerfile = path;
        return this;
    }

    public DockerBuildOptions WithBuildArg(string key, string value)
    {
        BuildArgs.Add($"{key}={value}");
        return this;
    }
}
```

### Step 3: Create the Operations Class

```csharp
// Operations/DockerOperations.cs
using Ando.Execution;
using Ando.Logging;
using Ando.References;
using Ando.Steps;

namespace Ando.Operations;

public class DockerOperations
{
    private readonly StepRegistry _registry;
    private readonly IBuildLogger _logger;
    private readonly Func<ICommandExecutor> _executorFactory;

    public DockerOperations(StepRegistry registry, IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    {
        _registry = registry;
        _logger = logger;
        _executorFactory = executorFactory;
    }

    // Factory method for creating references
    public DockerImageRef Image(string name, string tag = "latest")
    {
        return new DockerImageRef(name, tag);
    }

    // Operation that registers a step
    public void Build(DockerImageRef image, Action<DockerBuildOptions>? configure = null)
    {
        var options = new DockerBuildOptions();
        configure?.Invoke(options);

        _registry.Register("Docker.Build", async () =>
        {
            var args = new List<string> { "build", "-t", image.FullName };

            if (options.Dockerfile != null)
                args.AddRange(new[] { "-f", options.Dockerfile });

            foreach (var arg in options.BuildArgs)
                args.AddRange(new[] { "--build-arg", arg });

            args.Add(options.BuildContext ?? ".");

            var result = await _executorFactory().ExecuteAsync("docker", args.ToArray());
            return result.Success;
        }, image.FullName);
    }

    public void Push(DockerImageRef image)
    {
        _registry.Register("Docker.Push", async () =>
        {
            var result = await _executorFactory().ExecuteAsync("docker", new[] { "push", image.FullName });
            return result.Success;
        }, image.FullName);
    }
}
```

### Step 4: Register in BuildContext

Add the new operations to `BuildContext`:

```csharp
// Scripting/BuildContext.cs
public class BuildContext
{
    public BuildContextObject Context { get; }
    public DotnetOperations Dotnet { get; }
    public EfOperations Ef { get; }
    public NpmOperations Npm { get; }
    public DockerOperations Docker { get; }  // Add this

    public BuildContext(string rootPath, IBuildLogger logger)
    {
        Context = new BuildContextObject(rootPath);
        StepRegistry = new StepRegistry();
        Logger = logger;
        Executor = new ProcessRunner(logger);

        Dotnet = new DotnetOperations(StepRegistry, logger, () => Executor);
        Ef = new EfOperations(StepRegistry, logger, () => Executor);
        Npm = new NpmOperations(StepRegistry, logger, () => Executor);
        Docker = new DockerOperations(StepRegistry, logger, () => Executor);  // Add this
    }
}
```

### Step 5: Expose in ScriptGlobals

```csharp
// Scripting/ScriptGlobals.cs
public class ScriptGlobals
{
    public BuildContextObject Context { get; }
    public DotnetOperations Dotnet { get; }
    public EfOperations Ef { get; }
    public NpmOperations Npm { get; }
    public DockerOperations Docker { get; }  // Add this

    public ScriptGlobals(BuildContext buildContext)
    {
        // ... existing code ...
        Docker = buildContext.Docker;  // Add this
    }
}
```

### Step 6: Usage in build.ando

```csharp
var WebApi = Project.From("./src/WebApi/WebApi.csproj");
var ApiImage = Docker.Image("myregistry/webapi", "1.0.0");

Workflow("ci", w => {
    w.Configuration = Configuration.Release;
});

Dotnet.Restore(WebApi);
Dotnet.Build(WebApi);
Dotnet.Test(WebApi);
Dotnet.Publish(WebApi, o => o.Output(Context.Paths.Artifacts / "publish"));

Docker.Build(ApiImage, o => o
    .WithDockerfile("./src/WebApi/Dockerfile")
    .WithBuildArg("VERSION", "1.0.0"));

Docker.Push(ApiImage);
```

## Testing Operations

### Unit Test Pattern

```csharp
public class DockerOperationsTests
{
    private readonly StepRegistry _registry = new();
    private readonly TestLogger _logger = new();
    private readonly MockExecutor _executor = new();

    private DockerOperations CreateDocker() =>
        new DockerOperations(_registry, _logger, () => _executor);

    [Fact]
    public void Build_RegistersStep()
    {
        var docker = CreateDocker();
        var image = docker.Image("myapp", "1.0");

        docker.Build(image);

        Assert.Single(_registry.Steps);
        Assert.Equal("Docker.Build", _registry.Steps[0].Name);
        Assert.Equal("myapp:1.0", _registry.Steps[0].Context);
    }

    [Fact]
    public async Task Build_ExecutesCorrectCommand()
    {
        var docker = CreateDocker();
        var image = docker.Image("myapp", "1.0");

        docker.Build(image, o => o.WithBuildArg("VERSION", "1.0"));

        await _registry.Steps[0].Execute();

        Assert.Single(_executor.ExecutedCommands);
        var (command, args) = _executor.ExecutedCommands[0];
        Assert.Equal("docker", command);
        Assert.Contains("build", args);
        Assert.Contains("--build-arg", args);
    }
}
```

### MockExecutor for Testing

```csharp
public class MockExecutor : ICommandExecutor
{
    public List<(string Command, string[] Args)> ExecutedCommands { get; } = new();
    public bool SimulateFailure { get; set; }

    public Task<CommandResult> ExecuteAsync(string command, string[] args, CommandOptions? options = null)
    {
        ExecutedCommands.Add((command, args));
        return Task.FromResult(SimulateFailure
            ? CommandResult.Failed(1)
            : CommandResult.Ok());
    }

    public bool IsAvailable(string command) => true;
}
```

## CLI Commands

### Run Command

```bash
ando run [workflow] [options]
```

**Options:**
- `--verbosity <quiet|minimal|normal|detailed>` - Set logging verbosity
- `--no-color` - Disable colored output
- `--cold` - Always create fresh container
- `--image <image>` - Use custom Docker image
- `--dind` - Mount Docker socket for Docker-in-Docker

### Clean Command

```bash
ando clean [options]
```

**Options:**
- `--artifacts` - Remove artifacts directory
- `--temp` - Remove temp directory
- `--cache` - Remove NuGet and npm caches
- `--container` - Remove the project's warm container
- `--all` - Remove all of the above

## Design Principles

1. **Separation of Concerns**
   - Operations register steps, they don't execute them
   - WorkflowRunner handles execution, timing, and error handling
   - ContainerExecutor handles Docker execution
   - Logger handles all output formatting

2. **Isolated Execution**
   - All workflows run inside Docker containers
   - Project directory mounted at /workspace
   - Caches mounted for performance
   - Artifacts cleaned between runs

3. **Immutable References**
   - `ProjectRef`, `DockerImageRef`, etc. are immutable
   - Configuration is done through fluent options builders

4. **Fail-Fast by Default**
   - First failed step stops the workflow
   - Clear error messages with context
   - Real-time output streaming

5. **Testability**
   - All dependencies injected via constructor
   - ICommandExecutor interface allows mocking
   - Operations can be tested in isolation
   - MockExecutor for verifying command execution

6. **Extensibility without Modification**
   - New operations = new classes
   - No changes to core runner logic
   - Consistent patterns across all operation types
