# ADR-004: Secret Value Redaction in Logging

## Status
Accepted

## Context
Secrets retrieved via `VarsContext.EnvRequired()` could appear in log output, potentially exposing sensitive values in build logs and CI/CD artifacts. There was no mechanism to prevent secret values from being logged.

## Decision
Implement secret tracking and redaction:
- `VarsContext` tracks secret values when accessed via `EnvRequired()`
- `VarsContext.RegisterSecret()` allows manual registration of secrets
- `ConsoleLogger.AddSecrets()` accepts values to redact
- All logged messages are passed through `RedactSecrets()` which replaces secret values with `[REDACTED]`

## Consequences

### Positive
- Secrets are automatically protected from appearing in logs
- Works with both console output and log files
- Minimal performance impact (only redacts when secrets exist)
- Only redacts secrets >= 4 characters to avoid false positives

### Negative
- Secrets must be registered (either via EnvRequired or RegisterSecret)
- Very short secrets (< 4 chars) are not redacted
- String replacement may have performance impact with many secrets

## Implementation
- `src/Ando/Context/VarsContext.cs` - Tracks secret values
- `src/Ando/Logging/ConsoleLogger.cs` - Redacts secrets from output
- Redaction applies to Info, Warning, Error, and Debug messages
