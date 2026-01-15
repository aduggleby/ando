/**
 * Global Setup for Playwright E2E Tests
 *
 * Uses docker-compose to start an isolated test environment with:
 * - SQL Server container (private network, no exposed ports)
 * - Ando.Server container (exposes port 5000 only)
 *
 * The containers communicate over a private Docker network.
 */

import { execSync, spawnSync } from 'child_process';
import * as path from 'path';

const COMPOSE_FILE = path.resolve(__dirname, '../docker-compose.test.yml');
const SERVER_URL = 'http://localhost:17100/health';
const MAX_WAIT_SECONDS = 120;

/**
 * Check if the E2E containers are already running and healthy.
 */
function areContainersHealthy(): boolean {
  try {
    const result = spawnSync('docker', ['compose', '-f', COMPOSE_FILE, 'ps', '--format', 'json'], {
      encoding: 'utf-8',
      stdio: ['pipe', 'pipe', 'pipe'],
    });

    if (result.status !== 0) return false;

    const containers = result.stdout
      .trim()
      .split('\n')
      .filter(line => line)
      .map(line => JSON.parse(line));

    const sqlserver = containers.find((c: any) => c.Service === 'sqlserver');
    const server = containers.find((c: any) => c.Service === 'server');

    return (
      sqlserver?.Health === 'healthy' &&
      server?.Health === 'healthy'
    );
  } catch {
    return false;
  }
}

/**
 * Start the docker-compose services.
 */
function startContainers(): void {
  console.log('Starting E2E test containers...');
  execSync(`docker compose -f ${COMPOSE_FILE} up -d --build --wait`, {
    stdio: 'inherit',
    cwd: path.dirname(COMPOSE_FILE),
  });
}

/**
 * Wait for the server to be ready.
 */
async function waitForServer(): Promise<void> {
  console.log('Waiting for server to be ready...');

  for (let i = 0; i < MAX_WAIT_SECONDS; i++) {
    try {
      const response = await fetch(SERVER_URL);
      if (response.ok) {
        console.log('Server is ready!');
        return;
      }
    } catch {
      // Server not ready yet
    }

    if (i % 10 === 0) {
      console.log(`Waiting for server... (${i}s)`);
    }
    await new Promise(resolve => setTimeout(resolve, 1000));
  }

  throw new Error(`Server did not become ready within ${MAX_WAIT_SECONDS} seconds`);
}

/**
 * Global setup function called by Playwright.
 */
async function globalSetup(): Promise<void> {
  console.log('\n=== E2E Test Setup ===\n');

  if (areContainersHealthy()) {
    console.log('E2E containers are already running and healthy.');
  } else {
    startContainers();
  }

  await waitForServer();

  console.log('\n=== Setup Complete ===\n');
}

export default globalSetup;
