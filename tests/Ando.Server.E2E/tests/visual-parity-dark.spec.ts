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

async function setDark(page: any) {
  await page.evaluate(() => {
    localStorage.setItem('ando-theme', 'dark');
    document.documentElement.classList.add('theme-dark');
    document.documentElement.setAttribute('data-theme-mode', 'dark');
  });
}

async function capture(page: any, route: string, fileName: string, dir: string) {
  await page.goto(route);
  await setDark(page);
  await page.reload();
  await page.screenshot({ path: path.join(dir, `${sanitize(fileName)}.png`), fullPage: true });
}

test.describe('Visual Parity Dark', () => {
  test('captures key public and authenticated routes in dark mode', async ({ page, authedPage, testProject, testBuild }) => {
    const run = timestamp();
    const beforeDir = path.join(root, run, 'before', 'dark-desktop');
    ensureDir(beforeDir);

    await capture(page, '/auth/login', 'public-login-dark', beforeDir);
    await capture(page, '/auth/register', 'public-register-dark', beforeDir);
    await capture(page, '/auth/forgot-password', 'public-forgot-password-dark', beforeDir);

    await capture(authedPage, '/', 'authed-dashboard-dark', beforeDir);
    await capture(authedPage, '/projects', 'authed-projects-dark', beforeDir);
    await capture(authedPage, '/projects/status', 'authed-project-status-dark', beforeDir);
    await capture(authedPage, `/projects/${testProject.id}`, 'authed-project-details-dark', beforeDir);
    await capture(authedPage, `/projects/${testProject.id}/settings`, 'authed-project-settings-dark', beforeDir);
    await capture(authedPage, `/builds/${testBuild.id}`, 'authed-build-details-dark', beforeDir);
    await capture(authedPage, '/settings/api-tokens', 'authed-api-tokens-dark', beforeDir);

    expect(fs.existsSync(path.join(beforeDir, 'authed-dashboard-dark.png'))).toBeTruthy();
  });
});
