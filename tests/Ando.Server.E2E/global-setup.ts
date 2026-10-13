/**
 * Global Setup for Playwright E2E Tests
 *
 * Uses docker-compose to start an isolated test environment with:
 * - A database container (private network, no exposed ports):
 *   SQL Server by default, PostgreSQL when E2E_DB_PROVIDER=postgres
 * - Ando.Server container (exposes port 17110 only)
 *
 * The containers communicate over a private Docker network.
 *
 * Provider check:
 * After the server is ready, setup reads GET /health and requires its
 * `database` field to match the requested provider (SqlServer / PostgreSql).
 * A server that omits the field, or reports a different provider, fails setup.
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
import { getE2EDatabase } from './e2e-database';

const E2E_DATABASE = getE2EDatabase();
const COMPOSE_FILE = E2E_DATABASE.composeFile;
const COMPOSE_FILE_DISPLAY = path.relative(path.resolve(__dirname, '../..'), COMPOSE_FILE);
const MAX_WAIT_SECONDS = 60;

/**
 * Detect if we're running inside a Docker container.
 * The /.dockerenv file exists in Docker containers.
 */
function isInsideContainer(): boolean {
  return fs.existsSync('/.dockerenv');
}

/**
 * Check if Docker daemon is accessible.
 * Returns true if we can connect to the Docker daemon.
 */
function isDockerAvailable(): boolean {
  try {
    const result = spawnSync('docker', ['info'], {
      encoding: 'utf-8',
      stdio: ['pipe', 'pipe', 'pipe'],
      timeout: 5000,
    });
    return result.status === 0;
  } catch {
    return false;
  }
}

/**
 * Get the server URL for health checks.
 * Uses host.docker.internal when inside a container (ANDO build).
 */
function getServerUrl(): string {
  const port = 17110;
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

    const database = containers.find((c: any) => c.Service === E2E_DATABASE.databaseService);
    const server = containers.find((c: any) => c.Service === 'server');

    return (
      database?.Health === 'healthy' &&
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
    : `Check docker compose logs for errors: docker compose -f ${COMPOSE_FILE_DISPLAY} logs`;
  throw new Error(`Server did not become ready within ${MAX_WAIT_SECONDS} seconds at ${serverUrl}. ${hint}`);
}

/**
 * Verify the running server uses the requested database provider.
 * GET /health reports `database` as "SqlServer" or "PostgreSql". A missing
 * field fails too, so a server build without provider reporting cannot pass.
 */
async function assertServerDatabaseProvider(): Promise<void> {
  const serverUrl = getServerUrl();
  const expected = E2E_DATABASE.expectedHealthDatabase;

  const response = await fetch(serverUrl);
  if (!response.ok) {
    throw new Error(`Health check at ${serverUrl} returned ${response.status} while verifying the database provider.`);
  }

  const body: unknown = await response.json();
  const actual =
    typeof body === 'object' && body !== null ? (body as Record<string, unknown>).database : undefined;

  if (typeof actual !== 'string' || actual.length === 0) {
    throw new Error(
      `Health response from ${serverUrl} has no "database" field (got: ${JSON.stringify(body)}). ` +
      `Expected "${expected}" for E2E_DB_PROVIDER=${E2E_DATABASE.provider}. ` +
      'The server image is likely stale or predates provider reporting; rebuild it.'
    );
  }

  if (actual !== expected) {
    throw new Error(
      `E2E_DB_PROVIDER=${E2E_DATABASE.provider} requested "${expected}", but the server reports "${actual}". ` +
      `Check Database__Provider in ${COMPOSE_FILE_DISPLAY} and that a stale container from the other variant is not running.`
    );
  }

  console.log(`Server database provider verified: ${actual}`);
}

/**
 * Global setup function called by Playwright.
 */
async function globalSetup(): Promise<void> {
  console.log('\n=== E2E Test Setup ===\n');
  console.log(`Database provider: ${E2E_DATABASE.provider} (${COMPOSE_FILE_DISPLAY})`);

  const inContainer = isInsideContainer();
  const dockerAvailable = isDockerAvailable();

  if (inContainer) {
    console.log('Running inside container');

    if (!dockerAvailable) {
      // We're inside an ANDO container but Docker is not accessible.
      // This happens when running without --dind flag.
      throw new Error(
        'Docker is not available inside the container.\n' +
        'E2E tests require Docker-in-Docker mode to manage test containers.\n' +
        'Run with --dind flag: ando run --dind\n' +
        'Or run E2E tests directly on the host: cd tests/Ando.Server.E2E && npm test'
      );
    }

    console.log('Docker-in-Docker mode detected');
    console.log('Will recreate containers to ensure fresh state...');
  }

  if (!dockerAvailable) {
    throw new Error(
      'Docker is not available.\n' +
      'E2E tests require Docker to run the test environment.\n' +
      'Please ensure Docker is installed and running.'
    );
  }

  // When inside a container, always recreate to ensure fresh state.
  // The Docker daemon is shared, so we might see stale containers from host runs.
  if (inContainer) {
    stopContainers();
    startContainers();
  } else {
    // Even if containers are already healthy, still run `up --build` so the
    // server container reflects the current working tree (JS/CSS, migrations, etc.).
    if (areContainersHealthy()) {
      console.log('E2E containers are already running and healthy.');
      console.log('Rebuilding/restarting as needed to pick up local changes...');
    }
    startContainers();
  }

  await waitForServer();
  await assertServerDatabaseProvider();

  console.log('\n=== Setup Complete ===\n');
}

export default globalSetup;
