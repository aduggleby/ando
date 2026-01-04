# ANDO Refactoring Report

## Executive Summary

ANDO is a well-architected typed C# build system with clean separation of concerns, comprehensive documentation, and solid engineering practices. This report identifies architectural improvements, code quality enhancements, and technical debt that should be addressed to improve maintainability, security, and extensibility.

**Overall Assessment: A-** (Updated after Phase 1-3 refactoring)

| Category | Rating | Notes |
|----------|--------|-------|
| Architecture | A | Clean layering, clear separation |
| Code Quality | A- | Well-documented, reduced duplication |
| Error Handling | A- | Full exception context preserved |
| Testability | B+ | Improved with interface segregation |
| Security | A | Secret redaction implemented |
| Performance | B | Warm containers good, no parallelism |
| SOLID Compliance | A- | ISP, DIP improvements applied |

---

## Table of Contents

1. [Current Architecture Overview](#1-current-architecture-overview)
2. [High Priority Issues](#2-high-priority-issues)
3. [Medium Priority Issues](#3-medium-priority-issues)
4. [Low Priority Issues](#4-low-priority-issues)
5. [SOLID Principle Violations](#5-solid-principle-violations)
6. [Code Duplication Analysis](#6-code-duplication-analysis)
7. [Testability Improvements](#7-testability-improvements)
8. [Performance Opportunities](#8-performance-opportunities)
9. [Recommended Refactoring Roadmap](#9-recommended-refactoring-roadmap)
10. [Server-Specific Refactoring](#10-server-specific-refactoring)

---

## 1. Current Architecture Overview

### Layer Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                         CLI Layer (AndoCli)                      │
│        Argument parsing, Docker management, orchestration        │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Scripting Layer (Roslyn)                      │
│  ScriptHost, ScriptGlobals, BuildContext                         │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Operations Layer                              │
│  DotnetOperations, EfOperations, NpmOperations, AzureOperations  │
│  BicepOperations, FunctionsOperations, AppServiceOperations      │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Workflow Layer                                │
│  StepRegistry, BuildStep, WorkflowRunner, WorkflowResult         │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Execution Layer                               │
│  ICommandExecutor, ProcessRunner, ContainerExecutor              │
└──────────────────────────┬──────────────────────────────────────┘
                           │
┌──────────────────────────▼──────────────────────────────────────┐
│                    Infrastructure Layer                          │
│  DockerManager, ConsoleLogger, PathsContext, VarsContext         │
└─────────────────────────────────────────────────────────────────┘
```

### Key Architectural Patterns

1. **Step Registration Pattern**: Operations register steps during script execution; workflow executes them later
2. **Executor Factory Pattern**: `Func<ICommandExecutor>` allows runtime switching between local and container execution
3. **Warm Container Pattern**: Docker containers reused across builds for performance
4. **Fluent Builder Pattern**: Both `Action<Options>` callbacks and `ArgumentBuilder` for CLI construction

### Strengths

- Clean separation between script loading and execution phases
- Type-safe path composition with `BuildPath` value type
- Immutable result types (`CommandResult`, `StepResult`, `WorkflowResult`)
- Comprehensive file-level documentation
- Consistent naming conventions across operations

---

## 2. High Priority Issues

### 2.1 Secret Redaction Not Implemented

**Location**: `Context/VarsContext.cs`, `Logging/ConsoleLogger.cs`

**Problem**: `VarsContext` tracks secrets via `_secrets` HashSet and `EnvRequired()` method, but `ConsoleLogger` never uses this information. Sensitive values (API keys, connection strings) appear in plaintext in build logs.

```csharp
// VarsContext.cs - marks secrets
public string EnvRequired(string name)
{
    _secrets.Add(name);  // Tracked but unused
    return value;
}

// ConsoleLogger.cs - never redacts
public void Info(string message)
{
    WriteMessage(message);  // Secrets logged in plaintext
}
```

**Impact**: Security vulnerability - credentials exposed in build logs

**Recommendation**:
```csharp
// Pass secrets to ConsoleLogger
public class ConsoleLogger : IBuildLogger
{
    private readonly HashSet<string> _secretValues;

    public ConsoleLogger(LogLevel verbosity, HashSet<string>? secretValues = null)
    {
        _secretValues = secretValues ?? [];
    }

    private string RedactSecrets(string message)
    {
        var result = message;
        foreach (var secret in _secretValues.Where(s => !string.IsNullOrEmpty(s)))
        {
            result = result.Replace(secret, "***REDACTED***");
        }
        return result;
    }
}
```

**Effort**: 2-4 hours

---

### 2.2 No Command Logging Before Execution

**Location**: `Execution/ProcessRunner.cs`, `Execution/ContainerExecutor.cs`

**Problem**: Users cannot see what commands are being executed, making debugging difficult.

```csharp
// ProcessRunner.cs
public override async Task<CommandResult> ExecuteAsync(...)
{
    var startInfo = PrepareProcessStartInfo(command, args, options);
    // No logging of actual command
    using var process = Process.Start(startInfo);
    // ...
}
```

**Impact**:
- Difficult to debug failed builds
- Cannot copy/paste command to reproduce issues manually
- Users flying blind when builds fail

**Recommendation**:
```csharp
public override async Task<CommandResult> ExecuteAsync(...)
{
    var startInfo = PrepareProcessStartInfo(command, args, options);

    // Log command before execution
    var fullCommand = $"{command} {string.Join(" ", args)}";
    _logger.Debug($"Executing: {fullCommand}");
    if (options?.WorkingDirectory != null)
        _logger.Debug($"  Working directory: {options.WorkingDirectory}");

    using var process = Process.Start(startInfo);
    // ...
}
```

**Effort**: 1-2 hours

---

### 2.3 Path Translation Incomplete in ContainerExecutor

**Location**: `Execution/ContainerExecutor.cs:54-65`

**Problem**: `ConvertToContainerPath()` passes host absolute paths unchanged, which will fail inside the container.

```csharp
private string ConvertToContainerPath(string path)
{
    if (path.StartsWith("/workspace")) return path;
    if (!Path.IsPathRooted(path)) return $"{_containerWorkDir}/{path}";
    // For host absolute paths, we pass them through unchanged.
    // This is a simplification - proper path mapping would require
    // knowing the host project root to translate correctly.
    return path;  // BUG: Host path won't exist in container
}
```

**Impact**: Operations using absolute host paths fail silently or with confusing errors

**Recommendation**: Store host/container root mapping and translate paths:
```csharp
public class ContainerExecutor : CommandExecutorBase
{
    private readonly string _hostRootPath;
    private readonly string _containerRootPath = "/workspace";

    private string ConvertToContainerPath(string path)
    {
        if (path.StartsWith(_containerRootPath)) return path;
        if (!Path.IsPathRooted(path)) return $"{_containerWorkDir}/{path}";

        // Translate host absolute path to container path
        if (path.StartsWith(_hostRootPath))
        {
            var relativePath = Path.GetRelativePath(_hostRootPath, path);
            return $"{_containerRootPath}/{relativePath}";
        }

        throw new InvalidOperationException(
            $"Path '{path}' is outside the project root and cannot be accessed in the container.");
    }
}
```

**Effort**: 2-3 hours

---

## 3. Medium Priority Issues

### 3.1 Azure-Specific Logic in WorkflowRunner

**Location**: `Workflow/WorkflowRunner.cs:85-110`

**Problem**: `WorkflowRunner` contains Azure CLI availability checking, violating separation of concerns.

```csharp
private void CheckAndLogToolAvailability(string stepName)
{
    if (stepName.StartsWith("Azure.", StringComparison.OrdinalIgnoreCase) ||
        stepName.StartsWith("Bicep.", StringComparison.OrdinalIgnoreCase))
    {
        if (!AzureOperations.IsAzureCliAvailable())
        {
            _logger.Warning("Azure CLI not found...");
        }
    }
}
```

**Impact**:
- Hard to extend to other tools (Cloudflare, func CLI, etc.)
- Workflow layer coupled to specific operations
- Adding new tools requires modifying WorkflowRunner

**Recommendation**: Create a tool availability registry pattern:
```csharp
// New interface
public interface IToolAvailabilityChecker
{
    bool CanCheck(string stepName);
    bool IsAvailable();
    string GetInstallInstructions();
}

// Azure implementation
public class AzureToolChecker : IToolAvailabilityChecker
{
    public bool CanCheck(string stepName) =>
        stepName.StartsWith("Azure.") || stepName.StartsWith("Bicep.");
    public bool IsAvailable() => AzureOperations.IsAzureCliAvailable();
    public string GetInstallInstructions() => AzureOperations.GetAzureCliInstallInstructions();
}

// WorkflowRunner uses injected checkers
public class WorkflowRunner(IBuildLogger logger, IEnumerable<IToolAvailabilityChecker> checkers)
```

**Effort**: 4-6 hours

---

### 3.2 BuildContext is a God Object

**Location**: `Scripting/BuildContext.cs`

**Problem**: `BuildContext` holds too many responsibilities:
- Build state (Paths, Vars, Options)
- All operations (Dotnet, Ef, Npm, Azure, Bicep, Functions, AppService, Cloudflare, Artifacts)
- Executor management
- Docker management
- Artifact copying

```csharp
public class BuildContext
{
    public BuildContextObject Context { get; }
    public BuildOptions Options { get; }
    public DotnetOperations Dotnet { get; }
    public EfOperations Ef { get; }
    public NpmOperations Npm { get; }
    public AzureOperations Azure { get; }
    public BicepOperations Bicep { get; }
    public CloudflareOperations Cloudflare { get; }
    public FunctionsOperations Functions { get; }
    public AppServiceOperations AppService { get; }
    public ArtifactOperations Artifacts { get; }
    public StepRegistry StepRegistry { get; }
    public IBuildLogger Logger { get; }
    public ICommandExecutor Executor { get; private set; }
    // + Docker manager fields and methods
}
```

**Impact**:
- Difficult to test in isolation
- Changes to any operation affect the entire context
- Constructor keeps growing with each new operation

**Recommendation**: Extract operation registration to a factory:
```csharp
// OperationsFactory handles operation creation
public class OperationsFactory
{
    public static void RegisterAll(BuildContext context, StepRegistry registry,
        IBuildLogger logger, Func<ICommandExecutor> executorFactory)
    {
        context.Dotnet = new DotnetOperations(registry, logger, executorFactory);
        context.Ef = new EfOperations(registry, logger, executorFactory);
        // ...
    }
}

// Or use composition
public class BuildOperations
{
    public DotnetOperations Dotnet { get; }
    public EfOperations Ef { get; }
    // ... all operations
}

public class BuildContext
{
    public BuildContextObject Context { get; }
    public BuildOptions Options { get; }
    public BuildOperations Ops { get; }  // All operations grouped
    public StepRegistry StepRegistry { get; }
}
```

**Effort**: 6-8 hours

---

### 3.3 No Default Timeout for Commands

**Location**: `Execution/CommandOptions.cs`

**Problem**: Commands without explicit timeout can hang indefinitely.

```csharp
public class CommandOptions
{
    public int? TimeoutMs { get; set; }  // Optional, null = no timeout
}
```

**Impact**: A hung process (npm install with network issues, etc.) blocks the build forever

**Recommendation**:
```csharp
public class CommandOptions
{
    public const int DefaultTimeoutMs = 300_000; // 5 minutes

    public int TimeoutMs { get; set; } = DefaultTimeoutMs;

    public static CommandOptions NoTimeout => new() { TimeoutMs = int.MaxValue };
}
```

**Effort**: 1-2 hours

---

### 3.4 Limited Error Context in Workflow Execution

**Location**: `Workflow/WorkflowRunner.cs`

**Problem**: When steps fail, only `ex.Message` is captured, losing stack trace and inner exceptions.

```csharp
catch (Exception ex)
{
    stepStopwatch.Stop();
    logger.StepFailed(step.Name, stepStopwatch.Elapsed, ex.Message);
    // Stack trace lost, inner exceptions lost
}
```

**Impact**: Hard to diagnose root cause of failures

**Recommendation**:
```csharp
catch (Exception ex)
{
    stepStopwatch.Stop();
    logger.StepFailed(step.Name, stepStopwatch.Elapsed, ex.Message);

    // Always log full exception in debug mode
    logger.Debug($"Exception details: {ex}");

    // Store full exception in result
    stepResults.Add(new StepResult
    {
        StepName = step.Name,
        Success = false,
        Duration = stepStopwatch.Elapsed,
        ErrorMessage = ex.Message,
        Exception = ex  // New property
    });
}
```

**Effort**: 2-3 hours

---

## 4. Low Priority Issues

### 4.1 IBuildLogger Has Too Many Methods

**Location**: `Logging/IBuildLogger.cs`

**Problem**: Interface has 10 methods, violating Interface Segregation Principle.

**Recommendation**: Split into focused interfaces:
```csharp
public interface IMessageLogger
{
    LogLevel Verbosity { get; set; }
    void Info(string message);
    void Warning(string message);
    void Error(string message);
    void Debug(string message);
}

public interface IStepLogger
{
    void StepStarted(string stepName, string? context = null);
    void StepCompleted(string stepName, TimeSpan duration, string? context = null);
    void StepFailed(string stepName, TimeSpan duration, string? message = null);
    void StepSkipped(string stepName, string? reason = null);
}

public interface IWorkflowLogger
{
    void WorkflowStarted(string name, string? scriptPath, int totalSteps);
    void WorkflowCompleted(string name, TimeSpan duration, int stepsRun, int stepsFailed);
}

public interface IBuildLogger : IMessageLogger, IStepLogger, IWorkflowLogger { }
```

**Effort**: 4-6 hours (includes updating all usages)

---

### 4.2 Direct Process Management in Multiple Places

**Location**: `DockerManager.cs`, `ArtifactOperations.cs`

**Problem**: Process creation duplicated across files instead of using `ProcessRunner`.

```csharp
// DockerManager.cs line ~225
var process = Process.Start(new ProcessStartInfo
{
    FileName = "docker",
    Arguments = $"run -d ...",
    // Manual setup
});

// ArtifactOperations.cs line ~60
var process = Process.Start(new ProcessStartInfo
{
    FileName = "docker",
    Arguments = $"cp {containerId}:{sourcePath} {destPath}",
    // Manual setup
});
```

**Recommendation**: Use `ProcessRunner` consistently:
```csharp
public class DockerManager
{
    private readonly ProcessRunner _processRunner;

    public async Task<string> CreateContainerAsync(...)
    {
        var result = await _processRunner.ExecuteAsync("docker",
            ["run", "-d", ...]);
        return result.Output.Trim();
    }
}
```

**Effort**: 3-4 hours

---

### 4.3 Inconsistent Fluent Patterns Across Operations

**Location**: Various operation classes

**Problem**: Mix of fluent styles:
- `Dotnet.Restore(project, opt => opt.WithRuntime(...))` - Action callback
- `Npm.InDirectory(path).Install()` - Builder chain
- `Azure.LoginWithServicePrincipal(clientId, secret, tenant)` - Direct parameters

**Recommendation**: Standardize on Action callback for all operations:
```csharp
// Consistent pattern
Npm.Install(opt => opt.InDirectory("./frontend"));
Azure.LoginWithServicePrincipal(opt => opt
    .WithClientId(clientId)
    .WithClientSecret(secret)
    .WithTenantId(tenant));
```

**Effort**: 8-12 hours (breaking change)

---

### 4.4 No Architecture Decision Records (ADRs)

**Problem**: Design decisions documented in code comments but not in standalone documents.

**Recommendation**: Create `docs/adr/` directory with ADR files:
- `0001-step-registration-pattern.md`
- `0002-warm-container-reuse.md`
- `0003-executor-factory-pattern.md`
- `0004-fluent-builder-options.md`

**Effort**: 4-6 hours

---

## 5. SOLID Principle Violations

### Single Responsibility Principle (SRP)

| Class | Responsibilities | Recommendation |
|-------|-----------------|----------------|
| `AndoCli` | CLI parsing, Docker setup, script loading, build execution, artifact management | Extract `BuildOrchestrator` class |
| `BuildContext` | State management, operation factory, executor management, Docker integration | Split into `BuildState` + `OperationsFactory` |
| `DockerManager` | Container lifecycle, process execution, logging | Keep as-is (cohesive) |

### Open/Closed Principle (OCP)

| Component | Issue | Recommendation |
|-----------|-------|----------------|
| `WorkflowRunner` | Azure-specific code requires modification to add tools | Create `IToolAvailabilityChecker` interface |
| `StepRegistry` | No plugin mechanism | Add `IStepInterceptor` for cross-cutting concerns |

### Interface Segregation Principle (ISP)

| Interface | Methods | Recommendation |
|-----------|---------|----------------|
| `IBuildLogger` | 10 methods | Split into `IMessageLogger`, `IStepLogger`, `IWorkflowLogger` |

### Dependency Inversion Principle (DIP)

| Component | Issue | Recommendation |
|-----------|-------|----------------|
| `AndoCli` | Depends on concrete `DockerManager`, `ScriptHost` | Accept interfaces in constructor |
| `Operations` | Depend on concrete `StepRegistry` | Create `IStepRegistry` interface |

---

## 6. Code Duplication Analysis

### Argument Building Pattern

**Duplicated in**: All operation classes (~50+ occurrences)

```csharp
// Pattern repeated across all operations
RegisterCommand("Operation.Command", "tool",
    () => new ArgumentBuilder()
        .Add("command", "subcommand")
        .AddIfNotNull("--flag", options.Value)
        .AddFlag(options.Bool, "--bool-flag"),
    context);
```

**Recommendation**: Create command builder DSL:
```csharp
public class CommandRegistration
{
    public static CommandRegistration For(string name, string tool) => new(name, tool);

    public CommandRegistration WithArgs(params string[] args) { ... }
    public CommandRegistration WithOptional(string flag, string? value) { ... }
    public CommandRegistration WithFlag(bool condition, string flag) { ... }
    public CommandRegistration WithContext(string? context) { ... }

    public void Register(StepRegistry registry, Func<ICommandExecutor> executor) { ... }
}
```

---

### Environment Variable Retrieval

**Duplicated in**: `AzureOperations.cs`, `CloudflareOperations.cs`

```csharp
private static string GetRequiredEnv(string name)
{
    var value = Environment.GetEnvironmentVariable(name);
    if (string.IsNullOrEmpty(value))
        throw new InvalidOperationException($"Required environment variable '{name}' is not set.");
    return value;
}
```

**Recommendation**: Move to `VarsContext` or shared utility class

---

## 7. Testability Improvements

### Current Testing Gaps

| Component | Testability | Issue |
|-----------|-------------|-------|
| `ProcessRunner` | Low | Requires actual process execution |
| `ContainerExecutor` | Low | Requires Docker |
| `DockerManager` | Low | Requires Docker CLI |
| `ScriptHost` | Medium | Requires Roslyn + filesystem |
| `AndoCli` | Low | Requires full stack |

### Recommendations

1. **Extract File System Operations**:
```csharp
public interface IFileSystem
{
    bool FileExists(string path);
    string ReadAllText(string path);
    void WriteAllText(string path, string content);
}
```

2. **Make ProcessRunner Injectable**:
```csharp
public interface IProcessRunner
{
    Task<ProcessResult> RunAsync(string command, string[] args);
}

public class DockerManager(IProcessRunner processRunner)
```

3. **Add Integration Test Helpers**:
```csharp
public class TestContainerFixture : IAsyncLifetime
{
    public string ContainerId { get; private set; }
    public ContainerExecutor Executor { get; private set; }

    public async Task InitializeAsync()
    {
        // Create test container
    }
}
```

---

## 8. Performance Opportunities

### 8.1 No Parallel Step Execution

**Current**: Steps execute strictly sequentially

**Opportunity**: Allow parallel execution for independent steps

```csharp
// Future API possibility
Parallel.Group("Build Frontend and Backend", () =>
{
    Dotnet.Build(BackendProject);
    Npm.Build();  // Can run in parallel
});
```

**Effort**: 16-24 hours (significant architectural change)

### 8.2 No Incremental Build Support

**Current**: Every step runs on every build

**Opportunity**: Skip steps if inputs unchanged

```csharp
// Future API possibility
Dotnet.Build(project, opt => opt
    .WithInputHash("src/**/*.cs")
    .SkipIfUnchanged());
```

**Effort**: 24-40 hours (requires input tracking infrastructure)

### 8.3 Script Compilation Caching

**Current**: Roslyn compiles script on every run

**Opportunity**: Cache compiled script if unchanged

**Effort**: 8-12 hours

---

## 9. Recommended Refactoring Roadmap

### Phase 1: Security & Debugging (1-2 days) ✅ COMPLETED

| Task | Priority | Effort | Impact | Status |
|------|----------|--------|--------|--------|
| Implement secret redaction in ConsoleLogger | High | 4h | Security | ✅ Done |
| Add command logging before execution | High | 2h | Debugging | ✅ Done |
| Fix path translation in ContainerExecutor | High | 3h | Correctness | ✅ Done |

### Phase 2: Code Quality (3-5 days) ✅ COMPLETED

| Task | Priority | Effort | Impact | Status |
|------|----------|--------|--------|--------|
| Extract Azure logic from WorkflowRunner | Medium | 6h | Maintainability | ✅ Done |
| Add default command timeout | Medium | 2h | Reliability | ✅ Done |
| Improve error context in WorkflowRunner | Medium | 3h | Debugging | ✅ Done |
| Extract shared GetRequiredEnv utility | Low | 1h | DRY | ✅ Done |

### Phase 3: Architecture (1-2 weeks) ✅ COMPLETED

| Task | Priority | Effort | Impact | Status |
|------|----------|--------|--------|--------|
| Split IBuildLogger interface | Low | 6h | ISP compliance | ✅ Done |
| Extract BuildOperations from BuildContext | Medium | 8h | SRP compliance | ✅ Done |
| Create IStepRegistry interface | Low | 4h | DIP compliance | ✅ Done |
| Add ADR documentation | Low | 6h | Documentation | ✅ Done |

### Phase 4: Future Enhancements (Future Sprints)

| Task | Priority | Effort | Impact |
|------|----------|--------|--------|
| Parallel step execution | Low | 24h | Performance |
| Incremental build support | Low | 40h | Performance |
| Script compilation caching | Low | 12h | Performance |

---

## 10. Server-Specific Refactoring

The following items apply to `Ando.Server` specifically:

### Build Orchestration & Execution

1. **Split `BuildOrchestrator` into dedicated pipeline components**
   - Extract `RepositoryService`, `ContainerService`, `BuildRunner`, `ArtifactService`, `BuildStatusReporter`
   - Introduce `IBuildPipeline` interface for composing steps

2. **Replace raw `ProcessStartInfo` with shared process abstraction**
   - Create `Ando.Execution` shared library for both CLI and Server
   - Port CLI runner and add standardized result types

3. **Replace tuple return types with typed `BuildExecutionResult`**
   - Fields: `Success`, `StepsTotal`, `StepsCompleted`, `StepsFailed`, `Error`, `LogsSummary`

4. **Use structured output from CLI for step counts**
   - Emit JSON log events from CLI build runner
   - Server parses events for progress tracking

### Security & Secrets

5. **Decrypt secrets before injecting into container environment**
   - Use `IEncryptionService` before passing env vars
   - Add redaction layer in `ServerBuildLogger`

6. **Move secrets into mounted files or Docker secrets**
   - Write decrypted secrets to temporary folder
   - Mount read-only, expose paths in `Context.Vars`

### Data & Persistence

7. **Use short-lived DbContexts for long-running builds**
   - Use `IDbContextFactory<AndoDbContext>` per operation
   - Avoid tracking issues during long builds

8. **Separate build logs storage from transactional database**
   - Store logs in dedicated storage (file, blob, or log store)
   - Keep only pointers and summaries in DB

### Configuration & Hosting

9. **Extract configuration into extension methods**
   - Create `AddAndoServer()`, `AddHangfire()`, `UseAndoServer()`
   - Make environment-specific configuration explicit

10. **Make base URLs configurable**
    - Add `BaseUrl` setting for emails and GitHub status checks

### Reliability & Observability

11. **Add retry and circuit-breaking for external APIs**
    - Use Polly policies for GitHub and email services
    - Configure named HTTP clients with exponential backoff

12. **Formalize build state transitions in a state machine**
    - Centralize state transitions in `BuildStateMachine`
    - Validate allowed transitions

---

## Appendix: File-by-File Issues Summary

| File | Lines | Issues |
|------|-------|--------|
| `Cli/AndoCli.cs` | ~430 | God class, needs splitting |
| `Scripting/BuildContext.cs` | ~145 | God class, too many operations |
| `Workflow/WorkflowRunner.cs` | ~130 | Azure-specific code |
| `Execution/ContainerExecutor.cs` | ~80 | Incomplete path translation |
| `Logging/ConsoleLogger.cs` | ~160 | No secret redaction |
| `Context/VarsContext.cs` | ~80 | Unused secret tracking |
| `Operations/*.cs` | Various | Duplicated argument building |

---

## Conclusion

ANDO demonstrates solid software engineering with clean architecture, comprehensive documentation, and well-chosen design patterns. The identified issues are primarily:

1. **Security gap** in secret handling (high priority)
2. **Debugging difficulty** from missing command logging (high priority)
3. **Coupling issues** in WorkflowRunner and BuildContext (medium priority)
4. **Code duplication** in argument building (low priority)

The recommended approach is to address security and debugging issues immediately, then tackle architectural improvements incrementally without disrupting the stable foundation.

**Estimated Total Effort**:
- Phase 1 (Critical): 9 hours
- Phase 2 (Quality): 12 hours
- Phase 3 (Architecture): 24 hours
- Phase 4 (Future): 76+ hours
