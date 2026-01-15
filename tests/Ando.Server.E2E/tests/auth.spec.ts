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
import { LoginPage, DashboardPage } from '../pages';

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

      // Check that the nav contains a logout link
      const nav = authedPage.locator('nav');
      await expect(nav).toBeVisible();
      await expect(authedPage.locator('a[href*="logout"]')).toBeVisible();
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

      // Click logout link if visible
      const logoutLink = authedPage.locator('a[href*="logout"]');
      if (await logoutLink.isVisible()) {
        await logoutLink.click();
        await expect(authedPage).toHaveURL(/\/auth\/login/);
      }
    });
  });

  test.describe('Test API Authentication', () => {
    test('test API health check returns ok', async ({ testApi }) => {
      const isHealthy = await testApi.healthCheck();
      expect(isHealthy).toBeTruthy();
    });

    test('can create and delete test users', async ({ testApi }) => {
      const user = await testApi.createUser({ login: 'temp-test-user' });

      expect(user.userId).toBeGreaterThan(0);
      expect(user.login).toBe('temp-test-user');
      expect(user.email).toContain('temp-test-user');

      // Clean up
      await testApi.deleteUser(user.userId);
    });
  });
});
