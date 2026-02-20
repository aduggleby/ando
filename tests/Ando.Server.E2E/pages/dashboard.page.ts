import { Page, Locator, expect } from '@playwright/test';

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export class DashboardPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly recentBuildsSection: Locator;
  readonly recentBuildLinks: Locator;
  readonly emptyStateText: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'Dashboard', level: 1 });
    this.recentBuildsSection = page.locator('div').filter({
      has: page.getByRole('heading', { name: 'Recent Builds', level: 2 }),
    }).first();
    this.recentBuildLinks = page.locator('a[href^="/builds/"]');
    this.emptyStateText = page.getByText('No builds yet.');
  }

  async goto() {
    await this.page.goto('/');
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
  }

  private statValue(title: string): Locator {
    const titleRe = new RegExp(`^${escapeRegExp(title)}$`, 'i');
    const dt = this.page.locator('dt').filter({ hasText: titleRe }).first();
    return dt.locator('xpath=following-sibling::dd[1]');
  }

  async getProjectsCount(): Promise<string> {
    const value =
      (await this.statValue('Projects').textContent()) ??
      (await this.statValue('Total Projects').textContent());
    return value?.trim() || '0';
  }

  async getBuildsToday(): Promise<string> {
    return (await this.statValue('Builds Today').textContent())?.trim() || '0';
  }

  async getFailedToday(): Promise<string> {
    return (await this.statValue('Failed Today').textContent())?.trim() || '0';
  }

  async expectEmptyState() {
    await expect(this.emptyStateText).toBeVisible();
  }

  async expectRecentBuildsVisible() {
    await expect(this.recentBuildsSection).toBeVisible();
    await expect(this.recentBuildLinks.first()).toBeVisible();
  }

  async getRecentBuildCount(): Promise<number> {
    return this.recentBuildLinks.count();
  }

  async clickFirstBuild() {
    await this.recentBuildLinks.first().click();
  }

  getFailedTodayValueLocator(): Locator {
    return this.statValue('Failed Today');
  }
}
