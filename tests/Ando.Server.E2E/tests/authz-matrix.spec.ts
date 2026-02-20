import { test, expect } from '../fixtures/test-fixtures';

const TEST_API_KEY = 'test-api-key-for-e2e-tests';

test.describe('Authorization Matrix', () => {
  test('guest is denied protected APIs while authenticated cookie session is allowed', async ({ request, authedPage }) => {
    const guestProjects = await request.get('/api/projects');
    expect(guestProjects.status()).toBe(401);

    const userProjects = await authedPage.request.get('/api/projects');
    expect(userProjects.ok()).toBeTruthy();

    const userProjectsBody = await userProjects.json();
    expect(Array.isArray(userProjectsBody.projects)).toBeTruthy();
  });

  test('API token can authenticate protected APIs and revoke immediately blocks access', async ({ authedPage, request }) => {
    const createToken = await authedPage.request.post('/api/auth/tokens', {
      data: { name: `workflow-token-${Date.now()}` },
    });

    expect(createToken.ok()).toBeTruthy();
    const created = await createToken.json();
    expect(created.success).toBeTruthy();
    expect(created.token?.id).toBeGreaterThan(0);
    expect(typeof created.value).toBe('string');

    const tokenValue = created.value as string;
    const tokenId = created.token.id as number;

    const projectsWithBearer = await request.get('/api/projects', {
      headers: { Authorization: `Bearer ${tokenValue}` },
    });
    expect(projectsWithBearer.ok()).toBeTruthy();

    const meWithHeaderToken = await request.get('/api/auth/me', {
      headers: { 'X-Api-Token': tokenValue },
    });
    expect(meWithHeaderToken.ok()).toBeTruthy();
    const meBody = await meWithHeaderToken.json();
    expect(meBody.isAuthenticated).toBeTruthy();

    const revoke = await authedPage.request.delete(`/api/auth/tokens/${tokenId}`);
    expect(revoke.ok()).toBeTruthy();
    expect((await revoke.json()).success).toBeTruthy();

    const projectsAfterRevoke = await request.get('/api/projects', {
      headers: { Authorization: `Bearer ${tokenValue}` },
    });
    expect(projectsAfterRevoke.status()).toBe(401);
  });

  test('non-admin API token cannot access admin endpoints', async ({ authedPage, request }) => {
    const createToken = await authedPage.request.post('/api/auth/tokens', {
      data: { name: `non-admin-token-${Date.now()}` },
    });
    expect(createToken.ok()).toBeTruthy();
    const created = await createToken.json();
    expect(created.success).toBeTruthy();

    const adminResponse = await request.get('/api/admin/dashboard', {
      headers: { Authorization: `Bearer ${created.value}` },
    });
    expect([401, 403]).toContain(adminResponse.status());
  });

  test('admin-only APIs allow promoted user and deny after demotion', async ({ authedPage, authenticatedUser }) => {
    await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/role`, {
      headers: { 'X-Test-Api-Key': TEST_API_KEY },
      data: { role: 'Admin' },
    });

    await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/login`, {
      headers: { 'X-Test-Api-Key': TEST_API_KEY },
    });

    const adminDashboard = await authedPage.request.get('/api/admin/dashboard');
    expect(adminDashboard.ok()).toBeTruthy();

    await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/role`, {
      headers: { 'X-Test-Api-Key': TEST_API_KEY },
      data: { role: 'User' },
    });

    await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/login`, {
      headers: { 'X-Test-Api-Key': TEST_API_KEY },
    });

    const adminDashboardAfterDemotion = await authedPage.request.get('/api/admin/dashboard');
    expect([401, 403]).toContain(adminDashboardAfterDemotion.status());
  });

  test('admin users endpoint supports pagination and search filters', async ({ authedPage, authenticatedUser, testApi }) => {
    const userA = await testApi.createUser({ login: `matrix-alpha-${Date.now()}` });
    const userB = await testApi.createUser({ login: `matrix-beta-${Date.now()}` });

    try {
      await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/role`, {
        headers: { 'X-Test-Api-Key': TEST_API_KEY },
        data: { role: 'Admin' },
      });
      await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/login`, {
        headers: { 'X-Test-Api-Key': TEST_API_KEY },
      });

      const paged = await authedPage.request.get('/api/admin/users?page=1');
      expect(paged.ok()).toBeTruthy();
      const pagedBody = await paged.json();
      expect(pagedBody.currentPage).toBe(1);
      expect(pagedBody.pageSize).toBe(20);
      expect(pagedBody.users.length).toBeLessThanOrEqual(20);

      const searched = await authedPage.request.get(`/api/admin/users?page=1&pageSize=20&search=${encodeURIComponent(userA.email)}`);
      expect(searched.ok()).toBeTruthy();
      const searchedBody = await searched.json();
      expect(searchedBody.users.some((u: { email: string }) => u.email === userA.email)).toBeTruthy();
      expect(searchedBody.users.every((u: { email: string }) => u.email.includes(userA.email))).toBeTruthy();
    } finally {
      await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/role`, {
        headers: { 'X-Test-Api-Key': TEST_API_KEY },
        data: { role: 'User' },
      });
      await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/login`, {
        headers: { 'X-Test-Api-Key': TEST_API_KEY },
      });

      await testApi.deleteUser(userA.userId);
      await testApi.deleteUser(userB.userId);
    }
  });
});
