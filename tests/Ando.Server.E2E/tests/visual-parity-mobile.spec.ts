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

async function setTheme(page: { evaluate: (fn: () => void) => Promise<void> }, dark: boolean) {
  await page.evaluate(() => {
    localStorage.setItem('ando-theme', 'light');
    document.documentElement.classList.remove('theme-dark');
    document.documentElement.setAttribute('data-theme-mode', 'light');
  });

  if (dark) {
    await page.evaluate(() => {
      localStorage.setItem('ando-theme', 'dark');
      document.documentElement.classList.add('theme-dark');
      document.documentElement.setAttribute('data-theme-mode', 'dark');
    });
  }
}

async function capture(page: any, route: string, fileName: string, dir: string, dark = false) {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.goto(route);
  await setTheme(page, dark);
  await page.reload();
  await page.screenshot({
    path: path.join(dir, `${sanitize(fileName)}.png`),
    fullPage: true,
  });
}

test.describe('Visual Parity Mobile', () => {
  test('captures key routes in light and dark mobile layouts', async ({ page, authedPage, testProject, testBuild }) => {
    const run = timestamp();
    const beforeDir = path.join(root, run, 'before', 'mobile');
    ensureDir(beforeDir);

    await capture(page, '/auth/login', 'public-login-light-mobile', beforeDir, false);
    await capture(page, '/auth/login', 'public-login-dark-mobile', beforeDir, true);

    await capture(authedPage, '/', 'authed-dashboard-light-mobile', beforeDir, false);
    await capture(authedPage, '/', 'authed-dashboard-dark-mobile', beforeDir, true);
    await capture(authedPage, '/projects', 'authed-projects-light-mobile', beforeDir, false);
    await capture(authedPage, `/projects/${testProject.id}`, 'authed-project-detail-light-mobile', beforeDir, false);
    await capture(authedPage, `/builds/${testBuild.id}`, 'authed-build-detail-light-mobile', beforeDir, false);

    expect(fs.existsSync(path.join(beforeDir, 'public-login-light-mobile.png'))).toBeTruthy();
  });
});
