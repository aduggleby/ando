/**
 * Login Page Object Model
 *
 * Represents the login page and provides methods for authentication actions.
 */

import { Page, Locator, expect } from '@playwright/test';

export class LoginPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly loginButton: Locator;
  readonly description: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.locator('h1');
    this.loginButton = page.locator('a.btn-primary, button.btn-primary').filter({ hasText: /GitHub/i });
    this.description = page.locator('.login-box p');
  }

  async goto() {
    await this.page.goto('/auth/login');
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
    await expect(this.heading).toContainText(/login|sign in/i);
  }

  async expectLoginButtonVisible() {
    await expect(this.loginButton).toBeVisible();
  }

  async getLoginButtonHref(): Promise<string | null> {
    return this.loginButton.getAttribute('href');
  }
}
