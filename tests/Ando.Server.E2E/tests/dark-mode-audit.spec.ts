import fs from 'node:fs';
import path from 'node:path';
import type { Page } from '@playwright/test';
import { test, expect } from '../fixtures/test-fixtures';

const outputDir = path.join(process.cwd(), 'test-results', 'dark-mode-audit');

type ContrastIssue = {
  selector: string;
  text: string;
  ratio: number;
  color: string;
  background: string;
};

async function prepareDarkMode(page: Page) {
  await page.addInitScript(() => {
    localStorage.setItem('ando-theme', 'dark');
    document.documentElement.classList.add('theme-dark');
    document.documentElement.setAttribute('data-theme-mode', 'dark');
    document.documentElement.setAttribute('data-theme', 'dark');
  });
}

async function gotoAndCapture(
  page: Page,
  route: string,
  filename: string
) {
  await page.goto(route);
  await page.waitForLoadState('networkidle');
  await page.waitForTimeout(300);
  await page.screenshot({
    path: path.join(outputDir, filename),
    fullPage: true,
  });
}

async function collectContrastIssues(
  page: Page,
  routeName: string
): Promise<ContrastIssue[]> {
  const issues = await page.evaluate((name) => {
    type RGB = [number, number, number];

    function parseColor(value: string): { rgb: RGB; alpha: number } | null {
      const match = value.match(/rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/i);
      if (!match) return null;
      return {
        rgb: [Number(match[1]), Number(match[2]), Number(match[3])],
        alpha: match[4] ? Number(match[4]) : 1,
      };
    }

    function luminance([r, g, b]: RGB): number {
      const srgb = [r, g, b].map((v) => {
        const c = v / 255;
        return c <= 0.03928 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
      });
      return (0.2126 * srgb[0]) + (0.7152 * srgb[1]) + (0.0722 * srgb[2]);
    }

    function contrastRatio(fg: RGB, bg: RGB): number {
      const l1 = luminance(fg);
      const l2 = luminance(bg);
      const lighter = Math.max(l1, l2);
      const darker = Math.min(l1, l2);
      return (lighter + 0.05) / (darker + 0.05);
    }

    function effectiveBackgroundColor(el: Element): string {
      let current: Element | null = el;
      while (current) {
        const bg = getComputedStyle(current).backgroundColor;
        const parsed = parseColor(bg);
        if (parsed && parsed.alpha > 0.01) {
          return bg;
        }
        current = current.parentElement;
      }
      return 'rgb(15, 23, 42)';
    }

    function isVisible(el: Element): boolean {
      const style = getComputedStyle(el);
      if (style.display === 'none' || style.visibility === 'hidden' || Number(style.opacity) === 0) {
        return false;
      }
      const rect = (el as HTMLElement).getBoundingClientRect();
      return rect.width > 0 && rect.height > 0;
    }

    const selectors = [
      'h1', 'h2', 'h3', 'p', 'label', 'a', 'button', 'td', 'th',
      '.status-badge', '.status-badge-lg', '.deployment-badge',
      '.pr-badge', '.filter-count', '.btn-icon',
    ].join(',');

    const elements = Array.from(document.querySelectorAll(selectors));
    const low: Array<{
      selector: string;
      text: string;
      ratio: number;
      color: string;
      background: string;
    }> = [];

    for (const el of elements) {
      if (!isVisible(el)) continue;
      const text = (el.textContent || '').trim().replace(/\s+/g, ' ');
      if (!text) continue;

      const style = getComputedStyle(el);
      const fgRaw = style.color;
      const bgRaw = effectiveBackgroundColor(el);
      const fg = parseColor(fgRaw);
      const bg = parseColor(bgRaw);
      if (!fg || !bg) continue;

      const ratio = contrastRatio(fg.rgb, bg.rgb);
      if (ratio < 3) {
        low.push({
          selector: `${name}:${el.tagName.toLowerCase()}.${(el as HTMLElement).className || ''}`.slice(0, 180),
          text: text.slice(0, 80),
          ratio: Number(ratio.toFixed(2)),
          color: fgRaw,
          background: bgRaw,
        });
      }
    }

    return low;
  }, routeName);

  return issues;
}

test.describe('Dark Mode Visual Audit', () => {
  test.beforeAll(() => {
    fs.mkdirSync(outputDir, { recursive: true });
  });

  test('captures public auth pages in dark mode', async ({ page }) => {
    await prepareDarkMode(page);

    await gotoAndCapture(page, '/auth/login', 'public-login-dark.png');
    await gotoAndCapture(page, '/auth/register', 'public-register-dark.png');
    await gotoAndCapture(page, '/auth/forgot-password', 'public-forgot-password-dark.png');
  });

  test('captures core authenticated app pages in dark mode', async ({
    authedPage,
    testApi,
    testProject,
    testBuild,
  }) => {
    await prepareDarkMode(authedPage);

    const failedBuild = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Failed',
      commitMessage: 'Failed build for dark-mode visual audit',
    });

    const runningBuild = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Running',
      commitMessage: 'Running build for dark-mode visual audit',
    });

    await gotoAndCapture(authedPage, '/', 'authed-dashboard-dark.png');
    await gotoAndCapture(authedPage, '/projects', 'authed-projects-list-dark.png');
    await gotoAndCapture(authedPage, '/projects/status', 'authed-project-status-dark.png');
    await gotoAndCapture(authedPage, `/projects/${testProject.id}`, 'authed-project-details-dark.png');
    await gotoAndCapture(authedPage, `/projects/${testProject.id}/settings`, 'authed-project-settings-dark.png');
    await gotoAndCapture(authedPage, `/builds/${testBuild.id}`, 'authed-build-success-dark.png');
    await gotoAndCapture(authedPage, `/builds/${failedBuild.buildId}`, 'authed-build-failed-dark.png');
    await gotoAndCapture(authedPage, `/builds/${runningBuild.buildId}`, 'authed-build-running-dark.png');

    const contrastIssues: ContrastIssue[] = [];
    contrastIssues.push(...await collectContrastIssues(authedPage, 'build-running'));
    await authedPage.goto('/projects/status');
    contrastIssues.push(...await collectContrastIssues(authedPage, 'projects-status'));
    await authedPage.goto('/projects');
    contrastIssues.push(...await collectContrastIssues(authedPage, 'projects-list'));

    const reportPath = path.join(outputDir, 'contrast-issues.json');
    fs.writeFileSync(reportPath, JSON.stringify(contrastIssues, null, 2));

    const severeIssues = contrastIssues.filter((issue) => issue.ratio < 2.2);
    expect(
      severeIssues,
      `Found severe dark-mode contrast issues. See ${reportPath}`
    ).toEqual([]);
  });
});
