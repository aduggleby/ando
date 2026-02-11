/**
 * Projects Page Object Models
 *
 * Represents the project list, details, create, and settings pages.
 */

import { Page, Locator, expect } from '@playwright/test';

/**
 * Projects list page.
 */
export class ProjectsListPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly newProjectButton: Locator;
  readonly projectsGrid: Locator;
  readonly projectCards: Locator;
  readonly emptyState: Locator;
  readonly successAlert: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.locator('h1');
    this.newProjectButton = page.locator('a.btn-primary').filter({ hasText: /new project/i });
    this.projectsGrid = page.locator('.projects-unified-list');
    this.projectCards = page.locator('.project-row-card');
    this.emptyState = page.locator('.empty-state');
    this.successAlert = page.locator('.alert-success');
    this.errorAlert = page.locator('.alert-error');
  }

  async goto() {
    await this.page.goto('/projects');
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
    await expect(this.heading).toContainText('Projects');
  }

  async getProjectCount(): Promise<number> {
    return this.projectCards.count();
  }

  async expectEmptyState() {
    await expect(this.emptyState).toBeVisible();
  }

  async clickNewProject() {
    await this.newProjectButton.click();
    await this.page.waitForURL('**/projects/create**');
  }

  async clickProject(repoFullName: string) {
    const card = this.projectCards.filter({ hasText: repoFullName });
    await card.locator('a.btn').filter({ hasText: /open|view/i }).click();
  }

  async clickProjectSettings(repoFullName: string) {
    const card = this.projectCards.filter({ hasText: repoFullName });
    await card.locator('a.btn').filter({ hasText: /settings/i }).click();
  }

  async expectSuccessMessage(message: string | RegExp) {
    await expect(this.successAlert).toBeVisible();
    await expect(this.successAlert).toContainText(message);
  }

  async expectErrorMessage(message: string | RegExp) {
    await expect(this.errorAlert).toBeVisible();
    await expect(this.errorAlert).toContainText(message);
  }
}

/**
 * Project details page.
 */
export class ProjectDetailsPage {
  readonly page: Page;
  readonly breadcrumb: Locator;
  readonly triggerBuildButton: Locator;
  readonly settingsButton: Locator;
  readonly projectInfo: Locator;
  readonly buildsTable: Locator;
  readonly buildRows: Locator;
  readonly emptyBuildsState: Locator;
  readonly successAlert: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.breadcrumb = page.locator('.breadcrumb');
    this.triggerBuildButton = page.locator('button').filter({ hasText: /trigger build/i });
    this.settingsButton = page.locator('a.btn').filter({ hasText: /settings/i });
    this.projectInfo = page.locator('.project-info');
    this.buildsTable = page.locator('.builds-table');
    this.buildRows = page.locator('.build-row');
    this.emptyBuildsState = page.locator('.empty-state');
    this.successAlert = page.locator('.alert-success');
    this.errorAlert = page.locator('.alert-error');
  }

  async goto(projectId: number) {
    await this.page.goto(`/projects/${projectId}`);
  }

  async expectToBeVisible() {
    await expect(this.breadcrumb).toBeVisible();
    await expect(this.projectInfo).toBeVisible();
  }

  async getProjectName(): Promise<string> {
    const breadcrumbText = await this.breadcrumb.textContent();
    // Extract project name from breadcrumb
    const parts = breadcrumbText?.split('/') || [];
    return parts[parts.length - 1]?.trim() || '';
  }

  async getInfoValue(label: string): Promise<string> {
    const infoItem = this.projectInfo.locator('.info-item').filter({ hasText: new RegExp(label, 'i') });
    return (await infoItem.locator('.info-value').textContent()) || '';
  }

  async getBuildCount(): Promise<number> {
    return this.buildRows.count();
  }

  async clickTriggerBuild() {
    await this.triggerBuildButton.click();
  }

  async clickSettings() {
    await this.settingsButton.click();
    await this.page.waitForURL('**/settings**');
  }

  async clickBuild(buildId: number) {
    await this.page.locator(`tr.build-row`).nth(0).click();
  }

  async expectSuccessMessage(message: string | RegExp) {
    await expect(this.successAlert).toBeVisible();
    await expect(this.successAlert).toContainText(message);
  }
}

/**
 * Create project page.
 */
export class CreateProjectPage {
  readonly page: Page;
  readonly breadcrumb: Locator;
  readonly reposGrid: Locator;
  readonly repoCards: Locator;
  readonly emptyState: Locator;
  readonly successAlert: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.breadcrumb = page.locator('.breadcrumb');
    this.reposGrid = page.locator('.repos-grid');
    this.repoCards = page.locator('.repo-card');
    this.emptyState = page.locator('.empty-state');
    this.successAlert = page.locator('.alert-success');
    this.errorAlert = page.locator('.alert-error');
  }

  async goto() {
    await this.page.goto('/projects/create');
  }

  async expectToBeVisible() {
    await expect(this.breadcrumb).toBeVisible();
  }

  async getRepoCount(): Promise<number> {
    return this.repoCards.count();
  }

  async connectRepo(repoFullName: string) {
    const card = this.repoCards.filter({ hasText: repoFullName });
    await card.locator('button').filter({ hasText: /connect/i }).click();
  }

  async expectRepoAlreadyConnected(repoFullName: string) {
    const card = this.repoCards.filter({ hasText: repoFullName });
    await expect(card.locator('.connected-badge')).toBeVisible();
  }
}

/**
 * Project settings page.
 */
export class ProjectSettingsPage {
  readonly page: Page;
  readonly breadcrumb: Locator;

  // Build settings
  readonly branchFilterInput: Locator;
  readonly enablePrBuildsCheckbox: Locator;
  readonly timeoutInput: Locator;
  readonly dockerImageInput: Locator;
  readonly notifyOnFailureCheckbox: Locator;
  readonly notificationEmailInput: Locator;
  readonly saveSettingsButton: Locator;

  // Secrets
  readonly secretsTable: Locator;
  readonly secretNameInput: Locator;
  readonly secretValueInput: Locator;
  readonly addSecretButton: Locator;

  // Danger zone
  readonly deleteProjectButton: Locator;

  // Alerts
  readonly successAlert: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.breadcrumb = page.locator('.breadcrumb');

    // Build settings
    this.branchFilterInput = page.locator('#branchFilter');
    this.enablePrBuildsCheckbox = page.locator('input[name="enablePrBuilds"]');
    this.timeoutInput = page.locator('#timeoutMinutes');
    this.dockerImageInput = page.locator('#dockerImage');
    this.notifyOnFailureCheckbox = page.locator('input[name="notifyOnFailure"]');
    this.notificationEmailInput = page.locator('#notificationEmail');
    this.saveSettingsButton = page.locator('button').filter({ hasText: /save settings/i });

    // Secrets
    this.secretsTable = page.locator('.secrets-list');
    this.secretNameInput = page.locator('#secretName');
    this.secretValueInput = page.locator('#secretValue');
    this.addSecretButton = page.locator('.add-secret-form button').filter({ hasText: /add/i });

    // Danger zone
    this.deleteProjectButton = page.locator('.danger-zone button').filter({ hasText: /delete project/i });

    // Alerts
    this.successAlert = page.locator('.alert-success');
    this.errorAlert = page.locator('.alert-error');
  }

  async goto(projectId: number) {
    await this.page.goto(`/projects/${projectId}/settings`);
  }

  async expectToBeVisible() {
    await expect(this.breadcrumb).toBeVisible();
    await expect(this.branchFilterInput).toBeVisible();
  }

  async updateBranchFilter(value: string) {
    await this.branchFilterInput.fill(value);
  }

  async togglePrBuilds(enable: boolean) {
    const isChecked = await this.enablePrBuildsCheckbox.isChecked();
    if (isChecked !== enable) {
      await this.enablePrBuildsCheckbox.click();
    }
  }

  async updateTimeout(minutes: number) {
    await this.timeoutInput.fill(minutes.toString());
  }

  async updateDockerImage(image: string) {
    await this.dockerImageInput.fill(image);
  }

  async toggleNotifyOnFailure(enable: boolean) {
    const isChecked = await this.notifyOnFailureCheckbox.isChecked();
    if (isChecked !== enable) {
      await this.notifyOnFailureCheckbox.click();
    }
  }

  async updateNotificationEmail(email: string) {
    await this.notificationEmailInput.fill(email);
  }

  async saveSettings() {
    await this.saveSettingsButton.click();
  }

  async addSecret(name: string, value: string) {
    await this.secretNameInput.fill(name);
    await this.secretValueInput.fill(value);
    await this.addSecretButton.click();
  }

  async deleteSecret(name: string) {
    // Find the div containing this secret (has a code element with the name)
    const secretDiv = this.secretsTable.locator('> div').filter({ hasText: name });
    // Handle confirmation dialog
    this.page.once('dialog', dialog => dialog.accept());
    // Click the delete button (form submit button with trash icon)
    await secretDiv.locator('form button[type="submit"]').click();
  }

  async getSecretNames(): Promise<string[]> {
    // Secrets are displayed in divs with code elements containing the name
    const codes = this.secretsTable.locator('> div code').first();
    const items = this.secretsTable.locator('> div');
    const count = await items.count();
    const names: string[] = [];
    for (let i = 0; i < count; i++) {
      const text = await items.nth(i).locator('code').first().textContent();
      if (text) names.push(text);
    }
    return names;
  }

  async deleteProject() {
    // Handle confirmation dialog
    this.page.once('dialog', dialog => dialog.accept());
    await this.deleteProjectButton.click();
  }

  async expectSuccessMessage(message: string | RegExp) {
    await expect(this.successAlert).toBeVisible();
    await expect(this.successAlert).toContainText(message);
  }

  async expectErrorMessage(message: string | RegExp) {
    await expect(this.errorAlert).toBeVisible();
    await expect(this.errorAlert).toContainText(message);
  }
}
