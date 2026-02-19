/**
 * Register Page Object Model
 *
 * Represents the MVC server-rendered registration page and provides methods
 * for account creation.
 */

import { Page, Locator, expect } from '@playwright/test';

export class RegisterPage {
  readonly page: Page;
  readonly heading: Locator;
  readonly emailInput: Locator;
  readonly displayNameInput: Locator;
  readonly passwordInput: Locator;
  readonly confirmPasswordInput: Locator;
  readonly submitButton: Locator;
  readonly loginLink: Locator;
  readonly errorMessage: Locator;
  readonly validationErrors: Locator;

  constructor(page: Page) {
    this.page = page;
    this.heading = page.getByRole('heading', { level: 2 });
    this.emailInput = page.locator('input[type="email"]');
    this.displayNameInput = page.locator('input[type="text"]');
    this.passwordInput = page.locator('input[type="password"]').first();
    this.confirmPasswordInput = page.locator('input[type="password"]').nth(1);
    this.submitButton = page.locator('button[type="submit"]');
    this.loginLink = page.locator('a[href="/auth/login"]');
    this.errorMessage = page.locator('.bg-error-50, .dark\\:bg-error-500\\/10');
    this.validationErrors = page.locator('.bg-error-50, .dark\\:bg-error-500\\/10');
  }

  async goto() {
    await this.page.goto('/auth/register');
  }

  async expectToBeVisible() {
    await expect(this.heading).toBeVisible();
    await expect(this.heading).toContainText(/create your account/i);
  }

  async expectFormVisible() {
    await expect(this.emailInput).toBeVisible();
    await expect(this.passwordInput).toBeVisible();
    await expect(this.confirmPasswordInput).toBeVisible();
    await expect(this.submitButton).toBeVisible();
  }

  async register(email: string, password: string, displayName?: string) {
    await this.emailInput.fill(email);
    if (displayName) {
      await this.displayNameInput.fill(displayName);
    }
    await this.passwordInput.fill(password);
    await this.confirmPasswordInput.fill(password);
    await this.submitButton.click();
  }

  async expectErrorMessage(message: string | RegExp) {
    await expect(this.errorMessage).toBeVisible();
    await expect(this.errorMessage).toContainText(message);
  }

  async expectValidationError(message: string | RegExp) {
    await expect(this.page.getByText(message)).toBeVisible();
  }
}
