import { test, expect } from '../fixtures/test-fixtures';

test.describe('Edge and Error Cases', () => {
  test.describe('Compatibility Endpoints', () => {
    test('GET /session preserves query params via redirect', async ({ request }) => {
      const response = await request.get('/session?installation_id=42&setup_action=install', {
        maxRedirects: 0,
      });

      expect([302, 303, 307, 308]).toContain(response.status());
      const location = response.headers()['location'] || '';
      expect(location).toContain('/projects/create');
      expect(location).toContain('installation_id=42');
      expect(location).toContain('setup_action=install');
    });

    test('POST /session preserves query params via redirect', async ({ request }) => {
      const response = await request.post('/session?installation_id=99&setup_action=request', {
        maxRedirects: 0,
      });

      expect([302, 303, 307, 308]).toContain(response.status());
      const location = response.headers()['location'] || '';
      expect(location).toContain('/projects/create');
      expect(location).toContain('installation_id=99');
      expect(location).toContain('setup_action=request');
    });

    test('POST /webhooks/github rejects invalid signature', async ({ request }) => {
      const response = await request.post('/webhooks/github', {
        headers: {
          'X-GitHub-Event': 'push',
          'X-GitHub-Delivery': 'e2e-invalid-signature',
          'X-Hub-Signature-256': 'sha256=invalid',
          'Content-Type': 'application/json',
        },
        data: {
          repository: { id: 123, full_name: 'owner/repo' },
          after: '0123456789012345678901234567890123456789',
          ref: 'refs/heads/main',
        },
      });

      expect(response.status()).toBe(401);
      const body = await response.json();
      expect(String(body.error || '')).toMatch(/invalid signature/i);
    });

    test('/error returns problem response', async ({ request }) => {
      const response = await request.get('/error');
      expect(response.status()).toBe(500);
      expect(response.headers()['content-type'] || '').toContain('application/problem+json');
      const body = await response.json();
      expect(String(body.title || '')).toMatch(/unexpected error/i);
    });
  });

  test.describe('Admin API Authorization', () => {
    test('unauthenticated caller cannot access /api/admin/*', async ({ request }) => {
      const paths = [
        '/api/admin/dashboard',
        '/api/admin/users',
        '/api/admin/projects',
      ];

      for (const path of paths) {
        const response = await request.get(path);
        expect([401, 403, 404]).toContain(response.status());
      }
    });

    test('authenticated non-admin caller cannot access /api/admin/*', async ({ authedPage }) => {
      const paths = [
        '/api/admin/dashboard',
        '/api/admin/users',
        '/api/admin/projects',
      ];

      for (const path of paths) {
        const response = await authedPage.request.get(path);
        expect([401, 403, 404]).toContain(response.status());
      }
    });
  });

  test.describe('API Token Error Handling', () => {
    test('unauthenticated caller cannot list tokens', async ({ request }) => {
      const response = await request.get('/api/auth/tokens');
      expect([401, 404]).toContain(response.status());
    });

    test('authenticated caller can create/list/revoke tokens', async ({ authedPage }) => {
      const listBefore = await authedPage.request.get('/api/auth/tokens');
      expect(listBefore.ok()).toBeTruthy();
      const beforeBody = await listBefore.json();
      const beforeCount = Array.isArray(beforeBody.tokens) ? beforeBody.tokens.length : 0;

      const create = await authedPage.request.post('/api/auth/tokens', {
        data: { name: 'E2E token' },
      });
      expect(create.ok()).toBeTruthy();
      const created = await create.json();
      expect(created.success).toBeTruthy();
      expect(typeof created.value).toBe('string');
      expect(created.value).toContain('ando_pat_');
      expect(created.token?.id).toBeGreaterThan(0);

      const listAfterCreate = await authedPage.request.get('/api/auth/tokens');
      expect(listAfterCreate.ok()).toBeTruthy();
      const createdList = await listAfterCreate.json();
      const createdTokens = Array.isArray(createdList.tokens) ? createdList.tokens : [];
      expect(createdTokens.length).toBeGreaterThanOrEqual(beforeCount + 1);

      const revoke = await authedPage.request.delete(`/api/auth/tokens/${created.token.id}`);
      expect(revoke.ok()).toBeTruthy();
      const revoked = await revoke.json();
      expect(revoked.success).toBeTruthy();

      const listAfterRevoke = await authedPage.request.get('/api/auth/tokens');
      expect(listAfterRevoke.ok()).toBeTruthy();
      const revokedList = await listAfterRevoke.json();
      const target = (Array.isArray(revokedList.tokens) ? revokedList.tokens : [])
        .find((t: any) => t.id === created.token.id);
      expect(target).toBeTruthy();
      expect(target.revokedAtUtc || target.revokedAt).toBeTruthy();
    });

    test('token create rejects whitespace-only name', async ({ authedPage }) => {
      const response = await authedPage.request.post('/api/auth/tokens', {
        data: { name: '   ' },
      });

      expect(response.ok()).toBeTruthy();
      const body = await response.json();
      expect(body.success).toBeFalsy();
      expect(String(body.error || '').toLowerCase()).toContain('required');
    });

    test('revoke of unknown token id is a safe success response', async ({ authedPage }) => {
      const response = await authedPage.request.delete('/api/auth/tokens/999999999');
      expect(response.ok()).toBeTruthy();
      const body = await response.json();
      expect(body.success).toBeTruthy();
    });
  });

  test.describe('Test API Key Guard', () => {
    test('test mutation endpoint rejects missing API key', async ({ request }) => {
      const response = await request.post('/api/test/users', {
        data: {},
      });

      expect(response.status()).toBe(401);
      const body = await response.json();
      expect(String(body.error || '')).toMatch(/invalid api key/i);
    });
  });
});
