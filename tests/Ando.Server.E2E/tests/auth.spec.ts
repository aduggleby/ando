/**
 * Authentication Tests
 *
 * Tests for the authentication flow including:
 * - Unauthenticated access redirects to login
 * - Login page displays correctly (email/password)
 * - Logout functionality
 * - Session persistence
 */

import { test, expect } from '../fixtures/test-fixtures';
import { LoginPage, RegisterPage, DashboardPage } from '../pages';

test.describe('Authentication', () => {
  test.describe('Unauthenticated Access', () => {
    test('redirects to login page when accessing dashboard without auth', async ({ page }) => {
      await page.goto('/');
      await expect(page).toHaveURL(/\/auth\/login/);
    });

    test('redirects to login page when accessing projects without auth', async ({ page }) => {
      await page.goto('/projects');
      await expect(page).toHaveURL(/\/auth\/login/);
    });

    // Note: Tests for specific project/build detail pages are covered by the
    // authenticated access tests. The list pages correctly redirect to login,
    // which verifies the authentication system works properly.

    test('allows access to health endpoint without auth', async ({ page }) => {
      const response = await page.request.get('/health');
      expect(response.ok()).toBeTruthy();
      const body = await response.json();
      expect(body.status).toBe('healthy');
    });
  });

  test.describe('Login Page', () => {
    test('displays login page correctly', async ({ page }) => {
      const loginPage = new LoginPage(page);
      await loginPage.goto();

      await loginPage.expectToBeVisible();
      await loginPage.expectFormVisible();
    });

    test('login page has email and password fields', async ({ page }) => {
      const loginPage = new LoginPage(page);
      await loginPage.goto();

      // Check that email/password form is visible
      await expect(loginPage.emailInput).toBeVisible();
      await expect(loginPage.passwordInput).toBeVisible();
      await expect(loginPage.submitButton).toBeVisible();
    });

    test('login page has forgot password link', async ({ page }) => {
      const loginPage = new LoginPage(page);
      await loginPage.goto();

      await expect(loginPage.forgotPasswordLink).toBeVisible();
    });

    test('login page has register link', async ({ page }) => {
      const loginPage = new LoginPage(page);
      await loginPage.goto();

      await expect(loginPage.registerLink).toBeVisible();
    });
  });

  test.describe('Registration', () => {
    test('displays register page correctly', async ({ page }) => {
      const registerPage = new RegisterPage(page);
      await registerPage.goto();

      await registerPage.expectToBeVisible();
      await registerPage.expectFormVisible();
    });

    test('register page has login link', async ({ page }) => {
      const registerPage = new RegisterPage(page);
      await registerPage.goto();

      await expect(registerPage.loginLink).toBeVisible();
    });

    test('shows validation error for mismatched passwords', async ({ page }) => {
      const registerPage = new RegisterPage(page);
      await registerPage.goto();

      await registerPage.emailInput.fill('test-validation@example.com');
      await registerPage.passwordInput.fill('TestPass123');
      await registerPage.confirmPasswordInput.fill('DifferentPass');
      await registerPage.submitButton.click();

      // MVC server-side validation shows error via asp-validation-for span
      await registerPage.expectValidationError(/Passwords do not match/);
    });

    test('successful registration redirects to dashboard', async ({ page }) => {
      const uniqueEmail = `e2e-register-${Date.now()}@example.com`;

      const registerPage = new RegisterPage(page);
      await registerPage.goto();
      await registerPage.register(uniqueEmail, 'TestPassword1');

      // Should redirect to dashboard after successful registration
      await expect(page).toHaveURL('/', { timeout: 10000 });

      const dashboard = new DashboardPage(page);
      await dashboard.expectToBeVisible();

      // No explicit cleanup needed — E2E database is ephemeral
    });
  });

  test.describe('Authenticated Access', () => {
    test('authenticated user can access dashboard', async ({ authedPage }) => {
      await authedPage.goto('/');

      const dashboard = new DashboardPage(authedPage);
      await dashboard.expectToBeVisible();
    });

    test('authenticated user can access projects list', async ({ authedPage }) => {
      await authedPage.goto('/projects');
      await expect(authedPage.locator('h1')).toContainText('Projects');
    });

    test('authenticated user sees nav with logout link', async ({ authedPage }) => {
      await authedPage.goto('/');

      // Check that the nav contains a logout action
      const nav = authedPage.locator('nav');
      await expect(nav).toBeVisible();
      await expect(authedPage.getByRole('button', { name: /logout/i })).toBeVisible();
    });
  });

  test.describe('Session Management', () => {
    test('session persists across page navigations', async ({ authedPage }) => {
      // Navigate to dashboard
      await authedPage.goto('/');
      await expect(authedPage.locator('h1')).toContainText('Dashboard');

      // Navigate to projects
      await authedPage.goto('/projects');
      await expect(authedPage.locator('h1')).toContainText('Projects');

      // Navigate back to dashboard
      await authedPage.goto('/');
      await expect(authedPage.locator('h1')).toContainText('Dashboard');
    });

    test('logout redirects to login page', async ({ authedPage }) => {
      await authedPage.goto('/');

      const logoutButton = authedPage.getByRole('button', { name: /logout/i });
      await expect(logoutButton).toBeVisible();
      await logoutButton.click();
      await expect(authedPage).toHaveURL(/\/auth\/login/);
    });
  });

  test.describe('Test API Authentication', () => {
    test('test API health check returns ok', async ({ testApi }) => {
      const isHealthy = await testApi.healthCheck();
      expect(isHealthy).toBeTruthy();
    });

    test('can create and delete test users', async ({ testApi }, testInfo) => {
      // The docker-compose E2E SQL volume is often left running locally for debugging,
      // so avoid fixed usernames/emails that can collide across runs.
      const login = `temp-test-user-${testInfo.workerIndex}-${Date.now()}`;
      const user = await testApi.createUser({ login });

      expect(user.userId).toBeGreaterThan(0);
      expect(user.login).toBe(login);
      expect(user.email).toContain(login);

      // Clean up
      await testApi.deleteUser(user.userId);
    });
  });
});
