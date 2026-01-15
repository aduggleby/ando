import { defineConfig, devices } from '@playwright/test';

/**
 * Playwright configuration for Ando.Server E2E tests.
 *
 * Uses a test-specific base URL and ensures proper isolation between tests.
 * Requires a SQL Server container (ando-e2e-sqlserver) to be running.
 */
export default defineConfig({
  testDir: './tests',

  // Global setup starts docker-compose (SQL Server + server)
  globalSetup: './global-setup.ts',

  // Global teardown stops containers (only in CI or with CLEANUP=1)
  globalTeardown: './global-teardown.ts',

  // Run tests in parallel for speed
  fullyParallel: true,

  // Fail the build on CI if you accidentally left test.only in the source code
  forbidOnly: !!process.env.CI,

  // Retry failed tests on CI only
  retries: process.env.CI ? 2 : 0,

  // Limit parallel workers on CI
  workers: process.env.CI ? 1 : undefined,

  // Reporter configuration
  reporter: [
    ['html', { open: 'never' }],
    ['list'],
  ],

  // Shared settings for all projects
  use: {
    // Base URL for the test server (project port range: 17100-17199)
    baseURL: process.env.BASE_URL || 'http://localhost:17100',

    // Collect trace on first retry
    trace: 'on-first-retry',

    // Take screenshot on failure
    screenshot: 'only-on-failure',

    // Record video on failure
    video: 'on-first-retry',

    // Default timeout for actions
    actionTimeout: 10000,

    // Default navigation timeout
    navigationTimeout: 30000,
  },

  // Test timeout
  timeout: 60000,

  // Expect timeout
  expect: {
    timeout: 10000,
  },

  // Configure projects for major browsers
  projects: [
    {
      name: 'chromium',
      use: { ...devices['Desktop Chrome'] },
    },
    // Uncomment to test on Firefox and Safari
    // {
    //   name: 'firefox',
    //   use: { ...devices['Desktop Firefox'] },
    // },
    // {
    //   name: 'webkit',
    //   use: { ...devices['Desktop Safari'] },
    // },
  ],

  // Server is started by docker-compose in global-setup.ts
  // No webServer config needed - containers are managed externally
});
