/**
 * Login Page Object Model
 *
 * Represents the login page and provides methods for authentication actions.
 * Updated for email/password authentication.
 */

import { Page, Locator, expect } from '@playwright/test';

export class LoginPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly emailInput: Locator;
  readonly passwordInput: Locator;
  readonly submitButton: Locator;
  readonly forgotPasswordLink: Locator;
  readonly registerLink: Locator;
  readonly errorMessage: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.locator('h1');
    this.emailInput = page.locator('input[type="email"], input[name="Email"]');
    this.passwordInput = page.locator('input[type="password"], input[name="Password"]');
    this.submitButton = page.locator('button[type="submit"]');
    this.forgotPasswordLink = page.locator('a[href="/auth/forgot-password"]');
    this.registerLink = page.locator('a[href="/auth/register"]');
    this.errorMessage = page.locator('.alert-error');
  }

  async goto() {
    await this.page.goto('/auth/login');
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
    // Updated: new login page says "Welcome back"
    await expect(this.heading).toContainText(/welcome|login|sign in/i);
  }

  async expectFormVisible() {
    await expect(this.emailInput).toBeVisible();
    await expect(this.passwordInput).toBeVisible();
    await expect(this.submitButton).toBeVisible();
  }

  async login(email: string, password: string) {
    await this.emailInput.fill(email);
    await this.passwordInput.fill(password);
    await this.submitButton.click();
  }

  async expectErrorMessage(message: string | RegExp) {
    await expect(this.errorMessage).toBeVisible();
    await expect(this.errorMessage).toContainText(message);
  }
}
