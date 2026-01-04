# ADR-003: Default Command Timeout

## Status
Accepted

## Context
Commands executed via `CommandExecutorBase` had no default timeout. Commands without explicit timeout could hang indefinitely, blocking builds with no indication of the problem. This was particularly problematic in CI/CD environments where hung builds consume resources.

## Decision
Add a default timeout to `CommandOptions`:
- `DefaultTimeoutMs = 300_000` (5 minutes)
- `NoTimeout = -1` for explicitly disabling timeout
- `TimeoutMs` property defaults to `DefaultTimeoutMs`

## Consequences

### Positive
- Commands no longer hang indefinitely by default
- 5-minute timeout is reasonable for most build operations
- Explicit `NoTimeout` option for long-running operations
- Improved CI/CD reliability

### Negative
- Some long-running operations may timeout unexpectedly
- Users must explicitly set `NoTimeout` for operations like full database migrations

## Implementation
- `src/Ando/Execution/ICommandExecutor.cs` - Added constants and default
- `src/Ando/Execution/CommandExecutorBase.cs` - Updated timeout handling
- Timeout error message now includes the timeout value

## Migration
Existing code that relied on nullable `TimeoutMs` may need updates. Code that set `TimeoutMs = null` expecting no timeout should now use `TimeoutMs = CommandOptions.NoTimeout`.
