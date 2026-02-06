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
    this.heading = page.locator('h1');
    this.emailInput = page.locator('input#Email');
    this.displayNameInput = page.locator('input#DisplayName');
    this.passwordInput = page.locator('input#Password');
    this.confirmPasswordInput = page.locator('input#ConfirmPassword');
    this.submitButton = page.locator('button[type="submit"]');
    this.loginLink = page.locator('a[href="/auth/login"]');
    this.errorMessage = page.locator('.alert-error');
    this.validationErrors = page.locator('span.text-red-600');
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
    await expect(this.validationErrors.filter({ hasText: message })).toBeVisible();
  }
}
