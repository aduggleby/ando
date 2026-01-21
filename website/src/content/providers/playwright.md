---
title: Playwright
description: Run Playwright E2E tests for browser-based testing.
provider: Playwright
---

## Test Options

Configure Playwright test runs with `PlaywrightTestOptions`.

| Option | Description |
|--------|-------------|
| `Project` | Run tests for a specific project (e.g., "chromium", "firefox", "webkit") |
| `Headed` | Run tests in headed mode (visible browser windows) |
| `UI` | Run tests in UI mode for interactive debugging |
| `Workers` | Number of parallel workers (defaults to half of CPU cores) |
| `Reporter` | Reporter to use (e.g., "html", "list", "dot", "json") |
| `Grep` | Filter tests by title pattern |
| `UpdateSnapshots` | Update visual snapshots |
| `UseNpmScript` | Use `npm run` instead of `npx playwright test` |
| `NpmScriptName` | Custom npm script name when UseNpmScript is true (defaults to "test") |

## Example

Run E2E tests with Playwright.

```csharp
// Create a directory reference for E2E tests
var e2e = Directory("./tests/E2E");

// Install dependencies (prefer ci for reproducible builds)
Npm.Ci(e2e);

// Install Playwright browsers
Playwright.Install(e2e);

// Run all E2E tests
Playwright.Test(e2e);

// Run tests with options
Playwright.Test(e2e, o => {
    o.Project = "chromium";
    o.Headed = true;
    o.Workers = 4;
});
```
