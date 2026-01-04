# ADR-002: Tool Availability Registry Pattern

## Status
Accepted

## Context
`WorkflowRunner` contained hardcoded logic to check if Azure CLI was installed when Azure-related steps failed. This violated the Open/Closed Principle and made it difficult to add similar checks for other tools (Cloudflare wrangler, Azure Functions Core Tools, etc.).

## Decision
Implement a registry pattern for tool availability checks:
- `IToolAvailabilityChecker` interface defines the contract
- Concrete checkers (`AzureCliChecker`, `CloudflareChecker`, `FunctionsChecker`) implement the interface
- `ToolAvailabilityRegistry` holds all registered checkers
- `WorkflowRunner` queries the registry when steps fail

## Consequences

### Positive
- Adding new tool checks requires no changes to WorkflowRunner
- Tool-specific logic is encapsulated in dedicated checker classes
- Easy to test checkers in isolation
- Extensible for user-defined checkers

### Negative
- Slightly more indirection than inline code
- Registry pattern adds complexity

## Implementation
- `src/Ando/Workflow/IToolAvailabilityChecker.cs` - Interface
- `src/Ando/Workflow/ToolAvailabilityCheckers.cs` - Implementations and registry
- `WorkflowRunner.CheckAndLogToolAvailability()` - Uses registry
