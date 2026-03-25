import { test, expect } from '../fixtures/test-fixtures';
import routeContract from '../contracts/routes.contract.json';

interface ContractRoute {
  path: string;
  auth: 'public' | 'public_only' | 'required' | 'admin';
  fixture?: 'project' | 'build' | 'user';
}

function materializeRoute(route: ContractRoute, data: { projectId: number; buildId: number; userId: number }) {
  let path = route.path;
  if (route.fixture === 'project') {
    path = path.replace(':id', String(data.projectId));
  }
  if (route.fixture === 'build') {
    path = path.replace(':id', String(data.buildId));
  }
  if (route.fixture === 'user') {
    path = path.replace(':id', String(data.userId));
  }
  return path;
}

test.describe('Route Contract', () => {
  test('public and protected SPA route behavior matches contract for guest', async ({ page, testApi }) => {
    // Build route fixtures without authenticating the browser context.
    const user = await testApi.createUser();
    const project = await testApi.createProject({ userId: user.userId });
    const build = await testApi.createBuild({ projectId: project.projectId, status: 'Success' });

    const fixture = {
      projectId: project.projectId,
      buildId: build.buildId,
      userId: user.userId,
    };

    try {
      for (const route of routeContract.spaRoutes as ContractRoute[]) {
        const path = materializeRoute(route, fixture);
        await page.goto(path);

        if (route.auth === 'required' || route.auth === 'admin') {
          await expect(page).toHaveURL(/\/auth\/login(\?|$)/);
        } else {
          await expect(page).not.toHaveURL(/\/auth\/login\?returnUrl=.*auth\/(login|register)/);
        }
      }
    } finally {
      await testApi.deleteUser(user.userId);
    }
  });

  test('protected SPA routes are accessible to authenticated non-admin users', async ({ authedPage, testProject, testBuild, authenticatedUser }) => {
    const fixture = {
      projectId: testProject.id,
      buildId: testBuild.id,
      userId: authenticatedUser.id,
    };

    for (const route of routeContract.spaRoutes as ContractRoute[]) {
      const path = materializeRoute(route, fixture);
      await authedPage.goto(path);

      if (route.auth === 'required') {
        await expect(authedPage).toHaveURL(new RegExp(path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      }

      if (route.auth === 'public_only') {
        await expect(authedPage).toHaveURL('/');
      }

      if (route.auth === 'admin') {
        await expect(authedPage).not.toHaveURL(new RegExp(path.replace(/[.*+?^${}()|[\]\\]/g, '\\$&')));
      }
    }
  });

  test('infrastructure controller endpoints stay available', async ({ page }) => {
    for (const endpoint of routeContract.controllerRoutes as Array<{ method: string; path: string }>) {
      const method = endpoint.method.toUpperCase();
      const response =
        method === 'POST'
          ? await page.request.post(endpoint.path)
          : await page.request.get(endpoint.path);

      if (endpoint.path === '/webhooks/github') expect([400, 401]).toContain(response.status());
      else if (endpoint.path === '/error') expect(response.status()).toBe(500);
      else if (endpoint.path === '/health/docker') expect([200, 503]).toContain(response.status());
      else if (endpoint.path === '/health/github') expect([200, 503]).toContain(response.status());
      else if (endpoint.path === '/session') expect([200, 302, 303]).toContain(response.status());
      else expect(response.status()).toBe(200);
    }
  });

  test('critical API endpoints maintain authentication expectations', async ({ request, authedPage, testProject, testBuild }) => {
    const unauthenticatedProtected = ['/api/dashboard', '/api/projects', '/api/builds/1', '/api/auth/tokens'];

    for (const path of unauthenticatedProtected) {
      const response = await request.get(path);
      expect([401, 404]).toContain(response.status());
    }

    const authenticatedPaths = [
      '/api/dashboard',
      '/api/projects',
      '/api/projects/status',
      `/api/projects/${testProject.id}`,
      `/api/projects/${testProject.id}/settings`,
      `/api/builds/${testBuild.id}`,
      `/api/builds/${testBuild.id}/logs?afterSequence=0`,
      '/api/auth/me',
      '/api/auth/tokens',
      '/api/app/version',
    ];

    for (const path of authenticatedPaths) {
      const response = await authedPage.request.get(path);
      expect([200, 404]).toContain(response.status());
    }
  });
});
