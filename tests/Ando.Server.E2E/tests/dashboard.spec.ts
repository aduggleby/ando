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

      const createLink = authedPage.getByRole('link', { name: /create a project/i });
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
  });

  test.describe('With Builds', () => {
    test('shows recent builds', async ({ authedPage, testBuild }) => {
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
      expect(parseInt(buildsToday, 10)).toBeGreaterThan(0);
    });

    test('can navigate to build details from recent builds', async ({ authedPage, testBuild }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      await dashboard.clickFirstBuild();
      await expect(authedPage).toHaveURL(/\/builds\/\d+/);
    });
  });

  test.describe('With Failed Builds', () => {
    test('shows failed count highlighted in red', async ({ authedPage, testApi, testProject }) => {
      await testApi.createBuild({
        projectId: testProject.id,
        status: 'Failed',
        commitMessage: 'This build failed',
      });

      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const failedToday = await dashboard.getFailedToday();
      expect(parseInt(failedToday, 10)).toBeGreaterThan(0);

      await expect(dashboard.getFailedTodayValueLocator()).toHaveClass(/text-error/);
    });
  });

  test.describe('Navigation', () => {
    test('create project page shows add project form', async ({ authedPage }) => {
      await authedPage.goto('/projects/create');
      await expect(authedPage.getByRole('heading', { name: /add project/i, level: 1 })).toBeVisible();
      await expect(authedPage.getByLabel(/github repository url/i)).toBeVisible();
    });

    test('projects nav link navigates to projects list', async ({ authedPage, testProject }) => {
      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      await authedPage.getByRole('link', { name: /^projects$/i }).click();
      await expect(authedPage).toHaveURL(/\/projects/);
    });
  });

  test.describe('Statistics Accuracy', () => {
    test('multiple projects are counted correctly', async ({ authedPage, testApi, authenticatedUser }) => {
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
      const project1 = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'proj-1' });
      const project2 = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'proj-2' });

      await testApi.createBuild({ projectId: project1.projectId, status: 'Success' });
      await testApi.createBuild({ projectId: project1.projectId, status: 'Success' });
      await testApi.createBuild({ projectId: project2.projectId, status: 'Failed' });

      const dashboard = new DashboardPage(authedPage);
      await dashboard.goto();

      const buildsToday = await dashboard.getBuildsToday();
      expect(parseInt(buildsToday, 10)).toBe(3);

      const failedToday = await dashboard.getFailedToday();
      expect(parseInt(failedToday, 10)).toBe(1);
    });
  });
});
