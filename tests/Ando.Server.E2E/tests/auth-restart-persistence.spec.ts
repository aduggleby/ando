/**
 * Auth Restart Persistence Tests
 *
 * Validates that cookie authentication survives server container restart/recreate.
 *
 * This specifically guards against Data Protection key loss, which makes existing
 * auth cookies unreadable and results in "random logouts" after an update.
 */

import { test, expect } from '../fixtures/test-fixtures';
import { spawnSync } from 'child_process';
import * as path from 'path';

const COMPOSE_FILE = path.resolve(__dirname, '../../docker-compose.test.yml');

function dockerCompose(args: string[]) {
  const result = spawnSync('docker', ['compose', '-f', COMPOSE_FILE, ...args], {
    encoding: 'utf-8',
    stdio: ['ignore', 'pipe', 'pipe'],
    timeout: 120_000,
  });

  if (result.status !== 0) {
    throw new Error(
      `docker compose ${args.join(' ')} failed (status=${result.status}).\n` +
      `stdout:\n${result.stdout}\n` +
      `stderr:\n${result.stderr}\n`
    );
  }
}

async function waitForHealth(request: any, timeoutMs = 120_000) {
  const started = Date.now();
  while (Date.now() - started < timeoutMs) {
    try {
      const r = await request.get('/health');
      if (r.ok()) return;
    } catch {
      // ignore
    }
    await new Promise(r => setTimeout(r, 2000));
  }
  throw new Error('Server did not become healthy after restart');
}

test.describe('Auth Restart Persistence', () => {
  // This test restarts the shared docker-compose "server" service.
  // When the suite runs with multiple Playwright workers, that restart will
  // break other tests mid-flight (ECONNRESET / socket hang up). Only run this
  // in CI (where we force workers=1) or when explicitly enabled.
  test.skip(
    !process.env.CI && process.env.RUN_RESTART_PERSISTENCE_TEST !== '1',
    'Set RUN_RESTART_PERSISTENCE_TEST=1 to run locally (recommended with --workers=1).'
  );

  test('auth cookie survives server restart', async ({ authedPage }) => {
    const meBefore = await authedPage.request.get('/api/auth/me');
    expect(meBefore.ok()).toBeTruthy();
    const beforeJson = await meBefore.json();
    expect(beforeJson.isAuthenticated).toBeTruthy();

    dockerCompose(['restart', 'server']);

    await waitForHealth(authedPage.request);

    const meAfter = await authedPage.request.get('/api/auth/me');
    expect(meAfter.ok()).toBeTruthy();
    const afterJson = await meAfter.json();
    expect(afterJson.isAuthenticated).toBeTruthy();
  });
});
