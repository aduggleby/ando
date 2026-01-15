/**
 * Dashboard Page Object Model
 *
 * Represents the main dashboard page with statistics and recent builds.
 */

import { Page, Locator, expect } from '@playwright/test';

export class DashboardPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly newProjectButton: Locator;
  readonly projectsStatCard: Locator;
  readonly buildsStatCard: Locator;
  readonly failedStatCard: Locator;
  readonly successRateCard: Locator;
  readonly recentBuildsSection: Locator;
  readonly recentBuildsTable: Locator;
  readonly emptyState: Locator;
  readonly viewAllProjectsButton: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.locator('h1');
    this.newProjectButton = page.locator('a[href="/projects/create"].btn-primary').first();
    this.projectsStatCard = page.locator('.stat-card').filter({ hasText: /projects/i });
    this.buildsStatCard = page.locator('.stat-card').filter({ hasText: /builds today/i });
    this.failedStatCard = page.locator('.stat-card').filter({ hasText: /failed today/i });
    this.successRateCard = page.locator('.stat-card').filter({ hasText: /success rate/i });
    this.recentBuildsSection = page.locator('.recent-builds');
    this.recentBuildsTable = page.locator('.builds-table');
    this.emptyState = page.locator('.empty-state');
    this.viewAllProjectsButton = page.locator('a').filter({ hasText: /view all projects/i });
  }

  async goto() {
    await this.page.goto('/');
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
    await expect(this.heading).toContainText('Dashboard');
  }

  async getProjectsCount(): Promise<string> {
    const statValue = this.projectsStatCard.locator('.stat-value');
    return (await statValue.textContent()) || '0';
  }

  async getBuildsToday(): Promise<string> {
    const statValue = this.buildsStatCard.locator('.stat-value');
    return (await statValue.textContent()) || '0';
  }

  async getFailedToday(): Promise<string> {
    const statValue = this.failedStatCard.locator('.stat-value');
    return (await statValue.textContent()) || '0';
  }

  async getSuccessRate(): Promise<string> {
    const statValue = this.successRateCard.locator('.stat-value');
    return (await statValue.textContent()) || '-';
  }

  async expectEmptyState() {
    await expect(this.emptyState).toBeVisible();
  }

  async expectRecentBuildsVisible() {
    await expect(this.recentBuildsTable).toBeVisible();
  }

  async clickNewProject() {
    await this.newProjectButton.click();
    // Wait for navigation (may go to create page or login depending on GitHub connection)
    await this.page.waitForLoadState('networkidle');
  }

  async clickBuildRow(buildId: number) {
    await this.page.locator(`tr.build-row`).filter({ hasText: new RegExp(`${buildId}`) }).click();
    await this.page.waitForURL(`**/builds/${buildId}**`);
  }

  async getRecentBuildCount(): Promise<number> {
    return this.recentBuildsTable.locator('tbody tr').count();
  }
}
