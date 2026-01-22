/**
 * Global Setup for Playwright E2E Tests
 *
 * Uses docker-compose to start an isolated test environment with:
 * - SQL Server container (private network, no exposed ports)
 * - Ando.Server container (exposes port 17100 only)
 *
 * The containers communicate over a private Docker network.
 *
 * Container Detection:
 * When running inside an ANDO container (Docker-in-Docker mode), we use
 * host.docker.internal to reach containers running on the host's Docker.
 * This enables E2E tests to run as part of the ANDO build process.
 *
 * Important: When inside a container, we always recreate the test containers
 * to ensure a fresh state. This is because --dind mode shares the Docker
 * daemon, so we might see stale containers from previous host runs.
 */

import { execSync, spawnSync } from 'child_process';
import * as path from 'path';
import * as fs from 'fs';

const COMPOSE_FILE = path.resolve(__dirname, '../docker-compose.test.yml');
const MAX_WAIT_SECONDS = 60;

/**
 * Detect if we're running inside a Docker container.
 * The /.dockerenv file exists in Docker containers.
 */
function isInsideContainer(): boolean {
  return fs.existsSync('/.dockerenv');
}

/**
 * Get the server URL for health checks.
 * Uses host.docker.internal when inside a container (ANDO build).
 */
function getServerUrl(): string {
  const port = 17100;
  const host = isInsideContainer() ? 'host.docker.internal' : 'localhost';
  return `http://${host}:${port}/health`;
}

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
 * Stop and remove existing containers.
 */
function stopContainers(): void {
  console.log('Stopping existing E2E containers...');
  try {
    execSync(`docker compose -f ${COMPOSE_FILE} down --volumes --remove-orphans`, {
      stdio: 'inherit',
      cwd: path.dirname(COMPOSE_FILE),
    });
  } catch {
    // Ignore errors if containers don't exist
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
  const serverUrl = getServerUrl();
  console.log(`Waiting for server to be ready at ${serverUrl}...`);

  for (let i = 0; i < MAX_WAIT_SECONDS; i++) {
    try {
      const response = await fetch(serverUrl);
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

  const hint = isInsideContainer()
    ? 'Check that host.docker.internal resolves correctly and containers are accessible.'
    : 'Check docker compose logs for errors: docker compose -f tests/docker-compose.test.yml logs';
  throw new Error(`Server did not become ready within ${MAX_WAIT_SECONDS} seconds at ${serverUrl}. ${hint}`);
}

/**
 * Global setup function called by Playwright.
 */
async function globalSetup(): Promise<void> {
  console.log('\n=== E2E Test Setup ===\n');

  const inContainer = isInsideContainer();
  if (inContainer) {
    console.log('Running inside container (ANDO build with --dind)');
    console.log('Will recreate containers to ensure fresh state...');
  }

  // When inside a container, always recreate to ensure fresh state.
  // The Docker daemon is shared, so we might see stale containers from host runs.
  if (inContainer) {
    stopContainers();
    startContainers();
  } else if (areContainersHealthy()) {
    console.log('E2E containers are already running and healthy.');
  } else {
    startContainers();
  }

  await waitForServer();

  console.log('\n=== Setup Complete ===\n');
}

export default globalSetup;
