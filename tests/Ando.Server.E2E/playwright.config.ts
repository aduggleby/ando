import { defineConfig, devices } from '@playwright/test';
import * as fs from 'fs';

/**
 * Playwright configuration for Ando.Server E2E tests.
 *
 * Uses a test-specific base URL and ensures proper isolation between tests.
 * Requires a SQL Server container (ando-e2e-sqlserver) to be running.
 *
 * Container Detection:
 * When running inside an ANDO container (Docker-in-Docker mode), tests
 * connect via host.docker.internal instead of localhost. This allows
 * the tests to reach containers started on the host's Docker daemon.
 */

/**
 * Detect if we're running inside a Docker container.
 * The /.dockerenv file exists in Docker containers.
 */
function isInsideContainer(): boolean {
  return fs.existsSync('/.dockerenv');
}

/**
 * Get the appropriate base URL based on the environment.
 * - Inside container: use host.docker.internal to reach host's Docker
 * - Outside container: use localhost
 */
function getBaseUrl(): string {
  const port = 17100;
  const host = isInsideContainer() ? 'host.docker.internal' : 'localhost';
  return `http://${host}:${port}`;
}

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
    // Uses host.docker.internal when running inside a container (ANDO build)
    baseURL: process.env.BASE_URL || getBaseUrl(),

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
