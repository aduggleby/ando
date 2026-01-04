/**
 * Dashboard Tests
 *
 * Tests for the main dashboard including:
 * - Statistics display (projects, builds, failures, success rate)
 * - Recent builds list
 * - Navigation to projects and builds
 * - Empty state handling
 */

import { test, expect } from '../fixtures/test-fixtures';
import { DashboardPage } from '../pages';

test.describe('Dashboard', () => {
  test.describe('Empty State', () => {
    test('shows empty state when user has no projects', async ({ authedPage }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      await dashboard.expectToBeVisible();
      await dashboard.expectEmptyState();
    });

    test('shows zero for all stats when no projects', async ({ authedPage }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const projectsCount = await dashboard.getProjectsCount();
      const buildsToday = await dashboard.getBuildsToday();
      const failedToday = await dashboard.getFailedToday();

      expect(projectsCount).toBe('0');
      expect(buildsToday).toBe('0');
      expect(failedToday).toBe('0');
    });

    test('empty state has link to create project', async ({ authedPage }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const createLink = authedPage.locator('.empty-state a').filter({ hasText: /connect repository/i });
      await expect(createLink).toBeVisible();
    });
  });

  test.describe('With Projects', () => {
    test('shows correct project count', async ({ authedPage, testProject }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const projectsCount = await dashboard.getProjectsCount();
      expect(projectsCount).toBe('1');
    });

    test('shows view all projects button when projects exist', async ({ authedPage, testProject }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      await expect(dashboard.viewAllProjectsButton).toBeVisible();
    });
  });

  test.describe('With Builds', () => {
    test('shows recent builds in table', async ({ authedPage, testBuild }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      await dashboard.expectRecentBuildsVisible();
      const buildCount = await dashboard.getRecentBuildCount();
      expect(buildCount).toBeGreaterThan(0);
    });

    test('shows correct build count for today', async ({ authedPage, testBuild }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const buildsToday = await dashboard.getBuildsToday();
      expect(parseInt(buildsToday)).toBeGreaterThan(0);
    });

    test('shows success rate when builds exist', async ({ authedPage, testBuild }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const successRate = await dashboard.getSuccessRate();
      expect(successRate).toMatch(/\d+%|-/);
    });

    test('can navigate to build details from recent builds', async ({ authedPage, testBuild }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      // Click on the first build row
      await authedPage.locator('.build-row').first().click();
      await expect(authedPage).toHaveURL(/\/builds\/\d+/);
    });
  });

  test.describe('With Failed Builds', () => {
    test('shows failed count highlighted in red', async ({ authedPage, testApi, testProject }) => {
      // Create a failed build
      await testApi.createBuild({
        projectId: testProject.id,
        status: 'Failed',
        commitMessage: 'This build failed',
      });

      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const failedToday = await dashboard.getFailedToday();
      expect(parseInt(failedToday)).toBeGreaterThan(0);

      // Check that failed count has error styling
      const failedValue = dashboard.failedStatCard.locator('.stat-value');
      await expect(failedValue).toHaveClass(/text-error/);
    });

    test('success rate reflects failed builds', async ({ authedPage, testApi, testProject }) => {
      // Create successful and failed builds
      await testApi.createBuild({
        projectId: testProject.id,
        status: 'Success',
      });
      await testApi.createBuild({
        projectId: testProject.id,
        status: 'Failed',
      });

      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const successRate = await dashboard.getSuccessRate();
      // With 1 success and 1 failure, rate should be 50%
      expect(successRate).toBe('50%');
    });
  });

  test.describe('Navigation', () => {
    test('new project button navigates to create page', async ({ authedPage }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      await dashboard.clickNewProject();
      await expect(authedPage).toHaveURL(/\/projects\/create/);
    });

    test('view all projects button navigates to projects list', async ({ authedPage, testProject }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      await dashboard.viewAllProjectsButton.click();
      await expect(authedPage).toHaveURL(/\/projects/);
    });
  });

  test.describe('Statistics Accuracy', () => {
    test('multiple projects are counted correctly', async ({ authedPage, testApi, authenticatedUser }) => {
      // Create multiple projects
      await testApi.createProject({ userId: authenticatedUser.id, repoName: 'repo-1' });
      await testApi.createProject({ userId: authenticatedUser.id, repoName: 'repo-2' });
      await testApi.createProject({ userId: authenticatedUser.id, repoName: 'repo-3' });

      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const projectsCount = await dashboard.getProjectsCount();
      expect(projectsCount).toBe('3');
    });

    test('builds from multiple projects are aggregated', async ({
      authedPage,
      testApi,
      authenticatedUser,
    }) => {
      // Create two projects with builds
      const project1 = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'proj-1' });
      const project2 = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'proj-2' });

      await testApi.createBuild({ projectId: project1.projectId, status: 'Success' });
      await testApi.createBuild({ projectId: project1.projectId, status: 'Success' });
      await testApi.createBuild({ projectId: project2.projectId, status: 'Failed' });

      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const buildsToday = await dashboard.getBuildsToday();
      expect(parseInt(buildsToday)).toBe(3);

      const failedToday = await dashboard.getFailedToday();
      expect(parseInt(failedToday)).toBe(1);
    });
  });
});
