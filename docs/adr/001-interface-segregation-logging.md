# ADR-001: Interface Segregation for Logging

## Status
Accepted

## Context
The `IBuildLogger` interface combined three distinct concerns:
1. Message logging (Info, Warning, Error, Debug)
2. Step lifecycle events (StepStarted, StepCompleted, StepFailed, StepSkipped)
3. Workflow lifecycle events (WorkflowStarted, WorkflowCompleted)

Components that only needed message logging (like `CommandExecutorBase`) were forced to depend on the full interface, violating the Interface Segregation Principle.

## Decision
Split `IBuildLogger` into three focused interfaces:
- `IMessageLogger`: Basic message logging with verbosity control
- `IStepLogger`: Build step lifecycle events
- `IWorkflowLogger`: Workflow lifecycle events

`IBuildLogger` extends all three for backward compatibility.

## Consequences

### Positive
- Components can depend on minimal interfaces they actually need
- Easier to create focused test doubles/mocks
- Clearer separation of concerns
- No breaking changes to existing code using IBuildLogger

### Negative
- Slightly more complex interface hierarchy
- Three interfaces to understand instead of one

## Implementation
- `src/Ando/Logging/IBuildLogger.cs` - Contains all four interfaces
- `ConsoleLogger` implements `IBuildLogger` (unchanged)
- Components can now accept more specific interfaces
