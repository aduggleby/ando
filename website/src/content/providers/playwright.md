---
title: Playwright
description: Run Playwright E2E tests for browser-based testing.
provider: Playwright
---

## Options Reference

### Playwright.Test Options

| Option | Description |
|--------|-------------|
| `Project` | Run tests for a specific browser project defined in playwright.config. Values: `"chromium"`, `"firefox"`, `"webkit"`, or custom project names. Runs all projects if not specified. |
| `Headed` | Run tests with visible browser windows instead of headless mode. Useful for debugging test failures locally. Not recommended for CI. |
| `UI` | Launch Playwright's interactive UI mode for debugging. Shows test execution in real-time with time-travel debugging. Only works locally, not in CI. |
| `Workers` | Number of parallel test workers. Defaults to half of CPU cores. Set to `1` for sequential execution when debugging flaky tests. |
| `Reporter` | Output format for test results. Options: `"html"` (interactive report), `"list"` (detailed console output), `"dot"` (minimal dots), `"json"` (machine-readable), `"junit"` (CI integration). |
| `Grep` | Filter tests by title pattern using regex. Only tests matching the pattern will run. Example: `"login"` runs all tests with "login" in the title. |
| `UpdateSnapshots` | Update visual comparison snapshots instead of comparing them. Use when intentionally changing UI and need to update baseline images. |
| `UseNpmScript` | Use `npm run {script}` instead of `npx playwright test`. Enable when your package.json has custom Playwright configuration in the test script. |
| `NpmScriptName` | Custom npm script name to run when `UseNpmScript` is true. Defaults to `"test"`. Use for projects with multiple test scripts (e.g., "test:e2e", "test:visual"). |

## Operations

### Playwright.Install

Installs Playwright browsers and system dependencies. Runs `npx playwright install --with-deps` which:
- Downloads browser binaries (Chromium, Firefox, WebKit)
- Installs required system packages (libgtk, libasound, libxrandr, etc.)

This is required before running tests, especially in CI/Docker environments where system dependencies may be missing.

### Playwright.Test

Runs Playwright tests. By default uses `npx playwright test`. Set `UseNpmScript = true` to use `npm run test` instead.

## Example

Run E2E tests with Playwright.

```csharp
// Create a directory reference for E2E tests
var e2e = Directory("./tests/E2E");

// Install dependencies (prefer ci for reproducible builds)
Npm.Ci(e2e);

// Install Playwright browsers AND system dependencies
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

## Docker/CI Considerations

When running in Docker or CI environments:

1. **System dependencies**: `Playwright.Install()` automatically installs required system packages via `--with-deps`
2. **Headless mode**: Tests run headless by default (no display required)
3. **Docker-in-Docker**: For tests that need docker-compose, see the [Playwright E2E Tests recipe](/recipes/playwright-e2e-tests)
