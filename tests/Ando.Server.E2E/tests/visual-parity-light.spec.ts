import { test, expect } from '../fixtures/test-fixtures';
import * as fs from 'fs';
import * as path from 'path';

const root = path.resolve('artifacts/change-attempts');

function timestamp() {
  return new Date().toISOString().replace(/[:.]/g, '-');
}

function ensureDir(dir: string) {
  fs.mkdirSync(dir, { recursive: true });
}

function sanitize(input: string) {
  return input.replace(/[^a-zA-Z0-9_-]/g, '_');
}

async function capture(page: { goto: (url: string) => Promise<unknown>; screenshot: (o: { path: string; fullPage: boolean }) => Promise<unknown> }, route: string, fileName: string, dir: string) {
  await page.goto(route);
  await expect.poll(async () => true).toBeTruthy();
  await page.screenshot({
    path: path.join(dir, `${sanitize(fileName)}.png`),
    fullPage: true,
  });
}

test.describe('Visual Parity Light', () => {
  test('captures public and authenticated routes in light theme', async ({ page, authedPage, testProject, testBuild }) => {
    const run = timestamp();
    const beforeDir = path.join(root, run, 'before', 'light-desktop');
    ensureDir(beforeDir);

    await capture(page, '/auth/login', 'public-login', beforeDir);
    await capture(page, '/auth/register', 'public-register', beforeDir);
    await capture(page, '/auth/forgot-password', 'public-forgot-password', beforeDir);

    await capture(authedPage, '/', 'authed-dashboard', beforeDir);
    await capture(authedPage, '/projects', 'authed-projects-list', beforeDir);
    await capture(authedPage, '/projects/status', 'authed-project-status', beforeDir);
    await capture(authedPage, `/projects/${testProject.id}`, 'authed-project-details', beforeDir);
    await capture(authedPage, `/projects/${testProject.id}/settings`, 'authed-project-settings', beforeDir);
    await capture(authedPage, `/builds/${testBuild.id}`, 'authed-build-details', beforeDir);
    await capture(authedPage, '/settings/api-tokens', 'authed-api-tokens', beforeDir);

    expect(fs.existsSync(path.join(beforeDir, 'authed-dashboard.png'))).toBeTruthy();
  });
});
