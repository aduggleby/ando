/**
 * Global Teardown for Playwright E2E Tests
 *
 * Stops and removes the docker-compose containers after tests complete.
 * By default, containers are left running for debugging. Set CLEANUP=1 to remove.
 */

import { execSync } from 'child_process';
import * as path from 'path';

const COMPOSE_FILE = path.resolve(__dirname, '../docker-compose.test.yml');

/**
 * Global teardown function called by Playwright.
 */
async function globalTeardown(): Promise<void> {
  // Only cleanup if explicitly requested (for CI) or if CLEANUP env var is set
  if (process.env.CI || process.env.CLEANUP === '1') {
    console.log('\n=== E2E Test Teardown ===\n');
    console.log('Stopping E2E test containers...');

    try {
      execSync(`docker compose -f ${COMPOSE_FILE} down -v`, {
        stdio: 'inherit',
        cwd: path.dirname(COMPOSE_FILE),
      });
      console.log('Containers stopped and removed.');
    } catch (error) {
      console.error('Failed to stop containers:', error);
    }

    console.log('\n=== Teardown Complete ===\n');
  } else {
    console.log('\nContainers left running for debugging. Run with CLEANUP=1 to remove.\n');
  }
}

export default globalTeardown;
