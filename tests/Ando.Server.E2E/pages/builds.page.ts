import { Page, Locator, expect } from '@playwright/test';

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export class BuildDetailsPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly statusBadge: Locator;
  readonly liveIndicator: Locator;
  readonly cancelButton: Locator;
  readonly retryButton: Locator;
  readonly artifactsSection: Locator;
  readonly artifactItems: Locator;
  readonly logContainer: Locator;
  readonly logEntries: Locator;
  readonly autoScrollToggle: Locator;
  readonly successAlert: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: /build #/i, level: 1 });
    this.statusBadge = page.locator('h1 + span').first();
    this.liveIndicator = page.getByText('Live').first();
    this.cancelButton = page.getByRole('button', { name: /cancel build/i });
    this.retryButton = page.getByRole('button', { name: /retry build/i });

    this.artifactsSection = page
      .getByRole('heading', { name: /artifacts/i, level: 2 })
      .locator('xpath=ancestor::div[contains(@class, "bg-white")][1]');
    this.artifactItems = this.artifactsSection.getByRole('link', { name: /download/i });

    this.logContainer = page.locator('div.bg-gray-900.font-mono').first();
    this.logEntries = this.logContainer.locator(':scope > div').filter({ hasNotText: /waiting for logs|no logs available/i });
    this.autoScrollToggle = page.locator('label:has-text("Auto-scroll") input[type="checkbox"]');

    this.successAlert = page.locator('.bg-success-50, .dark\\:bg-success-500\\/10');
    this.errorAlert = page.locator('.bg-error-50, .dark\\:bg-error-500\\/10');
  }

  async goto(buildId: number) {
    await this.page.goto(`/builds/${buildId}`);
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
    await expect(this.statusBadge).toBeVisible();
  }

  async getStatus(): Promise<string> {
    return (await this.statusBadge.textContent())?.trim().toUpperCase() || '';
  }

  async expectStatus(status: string) {
    await expect(this.statusBadge).toContainText(new RegExp(escapeRegExp(status), 'i'));
  }

  async expectLive() {
    await expect(this.liveIndicator).toBeVisible();
  }

  async expectNotLive() {
    await expect(this.liveIndicator).toBeHidden();
  }

  async expectCanCancel() {
    await expect(this.cancelButton).toBeVisible();
  }

  async expectCannotCancel() {
    await expect(this.cancelButton).toBeHidden();
  }

  async expectCanRetry() {
    await expect(this.retryButton).toBeVisible();
  }

  async expectCannotRetry() {
    await expect(this.retryButton).toBeHidden();
  }

  async cancel() {
    await this.cancelButton.click();
  }

  async retry() {
    await this.retryButton.click();
  }

  async getCommitSha(): Promise<string> {
    const meta = await this.page.locator('h1').locator('xpath=ancestor::div[1]/following-sibling::p[1]').textContent();
    const match = meta?.match(/·\s*([0-9a-f]{7,40})\s*$/i);
    return match?.[1] || '';
  }

  async getBranch(): Promise<string> {
    const meta = await this.page.locator('h1').locator('xpath=ancestor::div[1]/following-sibling::p[1]').textContent();
    const match = meta?.match(/·\s*([^·]+?)\s*·\s*[0-9a-f]{7,40}\s*$/i);
    return match?.[1]?.trim() || '';
  }

  async getTriggeredBy(): Promise<string> {
    const dt = this.page.locator('dt').filter({ hasText: /^Triggered By$/i }).first();
    return (await dt.locator('xpath=following-sibling::dd[1]').textContent())?.trim() || '';
  }

  async getArtifactCount(): Promise<number> {
    return this.artifactItems.count();
  }

  async getArtifactNames(): Promise<string[]> {
    const names = await this.artifactsSection.locator('p.text-sm.font-medium').allTextContents();
    return names.map((n) => n.trim()).filter(Boolean);
  }

  async expectArtifactSizeText(text: string | RegExp) {
    await expect(this.artifactsSection).toContainText(text);
  }

  async getLogCount(): Promise<number> {
    const lines = await this.logContainer.locator(':scope > div').allTextContents();
    return lines
      .map((line) => line.trim())
      .filter((line) => line.length > 0)
      .filter((line) => !/^(waiting for logs|no logs available)$/i.test(line)).length;
  }

  async expectLogEntry(message: string | RegExp) {
    await expect(this.logContainer).toContainText(message);
  }

  async waitForLogEntry(message: string | RegExp, timeout = 30000) {
    await expect(this.logContainer).toContainText(message, { timeout });
  }

  async toggleAutoScroll() {
    await this.autoScrollToggle.click();
  }

  async isAutoScrollEnabled(): Promise<boolean> {
    return this.autoScrollToggle.isChecked();
  }

  async expectSuccessMessage(message: string | RegExp) {
    await expect(this.successAlert).toContainText(message);
  }

  async expectErrorAlert(message: string | RegExp) {
    await expect(this.errorAlert).toContainText(message);
  }
}
