import { Page, Locator, expect } from '@playwright/test';

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
}

export class ProjectsListPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly newProjectButton: Locator;
  readonly projectLinks: Locator;
  readonly emptyState: Locator;
  readonly successAlert: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: 'Projects', level: 1 });
    this.newProjectButton = page.getByRole('link', { name: /add project|create your first project/i }).first();
    this.projectLinks = page.locator('a[href^="/projects/"]').filter({ hasText: /.+\/.+/ });
    this.emptyState = page.getByText('No projects yet.');
    this.successAlert = page.locator('.bg-success-50, .dark\\:bg-success-500\\/10');
    this.errorAlert = page.locator('.bg-error-50, .dark\\:bg-error-500\\/10');
  }

  async goto() {
    await this.page.goto('/projects');
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
  }

  async getProjectCount(): Promise<number> {
    return this.projectLinks.count();
  }

  async expectEmptyState() {
    await expect(this.emptyState).toBeVisible();
  }

  async clickProject(repoFullName: string) {
    await this.projectLinks.filter({ hasText: repoFullName }).first().click();
  }

  async clickProjectSettings(repoFullName: string) {
    const projectLink = this.projectLinks.filter({ hasText: repoFullName }).first();
    const href = await projectLink.getAttribute('href');
    if (!href) throw new Error(`Project link not found for ${repoFullName}`);
    await this.page.goto(`${href}/settings`);
  }

  async expectSuccessMessage(message: string | RegExp) {
    await expect(this.successAlert).toContainText(message);
  }

  async expectErrorMessage(message: string | RegExp) {
    await expect(this.errorAlert).toContainText(message);
  }
}

export class ProjectDetailsPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly triggerBuildButton: Locator;
  readonly settingsButton: Locator;
  readonly recentBuildsHeader: Locator;
  readonly buildLinks: Locator;
  readonly emptyBuildsState: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { level: 1 });
    this.triggerBuildButton = page.getByRole('button', { name: /trigger build/i });
    this.settingsButton = page.getByRole('link', { name: /settings/i });
    this.recentBuildsHeader = page.getByRole('heading', { name: /recent builds/i, level: 2 });
    this.buildLinks = page.locator('a[href^="/builds/"]');
    this.emptyBuildsState = page.getByText('No builds yet. Trigger a build to get started.');
  }

  async goto(projectId: number) {
    await this.page.goto(`/projects/${projectId}`);
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
  }

  async getProjectName(): Promise<string> {
    return (await this.heading.textContent())?.trim() || '';
  }

  async getStatValue(label: string): Promise<string> {
    const labelRe = new RegExp(`^${escapeRegExp(label)}$`, 'i');
    const dt = this.page.locator('dt').filter({ hasText: labelRe }).first();
    return (await dt.locator('xpath=following-sibling::dd[1]').textContent())?.trim() || '';
  }

  async getBuildCount(): Promise<number> {
    return this.buildLinks.count();
  }

  async clickTriggerBuild() {
    await this.triggerBuildButton.click();
  }

  async clickSettings() {
    await this.settingsButton.click();
    await this.page.waitForURL('**/settings');
  }

  async expectEmptyBuildsState() {
    await expect(this.emptyBuildsState).toBeVisible();
  }
}

export class CreateProjectPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly repoUrlInput: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: /add project/i, level: 1 });
    this.repoUrlInput = page.getByLabel(/github repository url/i);
  }

  async goto() {
    await this.page.goto('/projects/create');
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
    await expect(this.repoUrlInput).toBeVisible();
  }
}

export class ProjectSettingsPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly secretNameInput: Locator;
  readonly secretValueInput: Locator;
  readonly addSecretButton: Locator;
  readonly allSecretsSection: Locator;
  readonly deleteProjectButton: Locator;
  readonly confirmDeleteProjectButton: Locator;
  readonly successAlert: Locator;
  readonly errorAlert: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { name: /project settings/i, level: 1 });
    this.secretNameInput = page.getByLabel(/secret name/i);
    this.secretValueInput = page.getByLabel(/secret value/i);
    this.addSecretButton = page.getByRole('button', { name: /add secret/i });
    this.allSecretsSection = page
      .getByRole('heading', { name: /all secrets/i, level: 2 })
      .locator('xpath=ancestor::div[contains(@class, \"bg-white\")][1]');
    this.deleteProjectButton = page.getByRole('button', { name: /^delete project$/i });
    this.confirmDeleteProjectButton = page.getByRole('button', { name: /yes, delete project/i });
    this.successAlert = page.locator('.bg-success-50, .dark\\:bg-success-500\\/10');
    this.errorAlert = page.locator('.bg-error-50, .dark\\:bg-error-500\\/10');
  }

  async goto(projectId: number) {
    await this.page.goto(`/projects/${projectId}`);
    await this.page.getByRole('link', { name: /settings/i }).click();
    await this.page.waitForURL(`**/projects/${projectId}/settings`);
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
    await expect(this.secretNameInput).toBeVisible();
  }

  async addSecret(name: string, value: string) {
    await this.secretNameInput.fill(name);
    await this.secretValueInput.fill(value);
    await this.addSecretButton.click();
  }

  async deleteSecret(name: string) {
    const escapedName = escapeRegExp(name);
    const secretRow = this.page
      .locator('div')
      .filter({ hasText: new RegExp(`\\b${escapedName}\\b`) })
      .filter({ has: this.page.getByRole('button', { name: /delete/i }) })
      .first();

    this.page.once('dialog', (dialog) => dialog.accept());
    await secretRow.getByRole('button', { name: /delete/i }).click();
  }

  async getSecretNames(): Promise<string[]> {
    const names = await this.allSecretsSection.locator('p.text-sm.font-medium').allTextContents();
    return names.map((n) => n.trim()).filter(Boolean);
  }

  async deleteProject() {
    await this.deleteProjectButton.click();
    await this.confirmDeleteProjectButton.click();
  }

  async expectSuccessMessage(message: string | RegExp) {
    await expect(this.successAlert).toContainText(message);
  }

  async expectErrorMessage(message: string | RegExp) {
    await expect(this.errorAlert).toContainText(message);
  }
}
