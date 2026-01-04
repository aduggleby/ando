/**
 * Builds Page Object Model
 *
 * Represents the build details page with logs, artifacts, and actions.
 */

import { Page, Locator, expect } from '@playwright/test';

export class BuildDetailsPage {
  readonly page: Page;
  readonly breadcrumb: Locator;

  // Status
  readonly statusBadge: Locator;
  readonly liveIndicator: Locator;

  // Actions
  readonly cancelButton: Locator;
  readonly retryButton: Locator;

  // Build info
  readonly buildInfo: Locator;
  readonly commitSha: Locator;
  readonly branch: Locator;
  readonly commitMessage: Locator;
  readonly commitAuthor: Locator;
  readonly trigger: Locator;
  readonly duration: Locator;
  readonly errorMessage: Locator;

  // Artifacts
  readonly artifactsSection: Locator;
  readonly artifactItems: Locator;

  // Logs
  readonly logContainer: Locator;
  readonly logEntries: Locator;
  readonly scrollToggle: Locator;

  // Alerts
  readonly successAlert: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.breadcrumb = page.locator('.breadcrumb');

    // Status
    this.statusBadge = page.locator('#build-status, .build-status-large .status-badge');
    this.liveIndicator = page.locator('#live-indicator, .live-indicator');

    // Actions
    this.cancelButton = page.locator('button').filter({ hasText: /cancel build/i });
    this.retryButton = page.locator('button').filter({ hasText: /retry build/i });

    // Build info
    this.buildInfo = page.locator('.build-header');
    this.commitSha = page.locator('.commit-sha').first();
    this.branch = page.locator('.branch-name').first();
    this.commitMessage = page.locator('.commit-message').first();
    this.commitAuthor = page.locator('.info-item').filter({ hasText: /author/i }).locator('.info-value');
    this.trigger = page.locator('.info-item').filter({ hasText: /trigger/i }).locator('.info-value');
    this.duration = page.locator('#build-duration, .info-item').filter({ hasText: /duration/i }).locator('.info-value');
    this.errorMessage = page.locator('.error-message pre');

    // Artifacts
    this.artifactsSection = page.locator('.section').filter({ hasText: /artifacts/i });
    this.artifactItems = page.locator('.artifact-item');

    // Logs
    this.logContainer = page.locator('#log-container, .log-container');
    this.logEntries = page.locator('.log-entry');
    this.scrollToggle = page.locator('#scroll-toggle');

    // Alerts
    this.successAlert = page.locator('.alert-success');
    this.errorAlert = page.locator('.alert-error');
  }

  async goto(buildId: number) {
    await this.page.goto(`/builds/${buildId}`);
  }

  async expectToBeVisible() {
    await expect(this.breadcrumb).toBeVisible();
    await expect(this.statusBadge).toBeVisible();
  }

  async getStatus(): Promise<string> {
    return (await this.statusBadge.textContent())?.trim().toUpperCase() || '';
  }

  async expectStatus(status: string) {
    await expect(this.statusBadge).toContainText(new RegExp(status, 'i'));
  }

  async expectLive() {
    await expect(this.liveIndicator).toBeVisible();
  }

  async expectNotLive() {
    await expect(this.liveIndicator).not.toBeVisible();
  }

  async expectCanCancel() {
    await expect(this.cancelButton).toBeVisible();
  }

  async expectCannotCancel() {
    await expect(this.cancelButton).not.toBeVisible();
  }

  async expectCanRetry() {
    await expect(this.retryButton).toBeVisible();
  }

  async expectCannotRetry() {
    await expect(this.retryButton).not.toBeVisible();
  }

  async cancel() {
    await this.cancelButton.click();
  }

  async retry() {
    await this.retryButton.click();
  }

  async getCommitSha(): Promise<string> {
    return (await this.commitSha.textContent())?.trim() || '';
  }

  async getBranch(): Promise<string> {
    return (await this.branch.textContent())?.trim() || '';
  }

  async getTrigger(): Promise<string> {
    return (await this.trigger.textContent())?.trim() || '';
  }

  async getDuration(): Promise<string> {
    return (await this.duration.textContent())?.trim() || '';
  }

  async expectErrorMessage(message: string | RegExp) {
    await expect(this.errorMessage).toBeVisible();
    await expect(this.errorMessage).toContainText(message);
  }

  // Artifacts
  async getArtifactCount(): Promise<number> {
    return this.artifactItems.count();
  }

  async getArtifactNames(): Promise<string[]> {
    const count = await this.artifactItems.count();
    const names: string[] = [];
    for (let i = 0; i < count; i++) {
      const text = await this.artifactItems.nth(i).locator('.artifact-name').textContent();
      if (text) names.push(text.trim());
    }
    return names;
  }

  async downloadArtifact(name: string) {
    const item = this.artifactItems.filter({ hasText: name });
    const [download] = await Promise.all([
      this.page.waitForEvent('download'),
      item.locator('a').filter({ hasText: /download/i }).click(),
    ]);
    return download;
  }

  // Logs
  async getLogCount(): Promise<number> {
    return this.logEntries.count();
  }

  async expectLogEntry(message: string | RegExp) {
    await expect(this.logContainer).toContainText(message);
  }

  async expectLogEntryType(type: string, message: string | RegExp) {
    const entry = this.logEntries.filter({ hasClass: `log-${type.toLowerCase()}` }).filter({ hasText: message });
    await expect(entry).toBeVisible();
  }

  async waitForLogEntry(message: string | RegExp, timeout = 30000) {
    await expect(this.logContainer).toContainText(message, { timeout });
  }

  async toggleAutoScroll() {
    await this.scrollToggle.click();
  }

  async expectSuccessMessage(message: string | RegExp) {
    await expect(this.successAlert).toBeVisible();
    await expect(this.successAlert).toContainText(message);
  }

  async expectErrorAlert(message: string | RegExp) {
    await expect(this.errorAlert).toBeVisible();
    await expect(this.errorAlert).toContainText(message);
  }
}
