import { test, expect } from '../fixtures/test-fixtures';

test.describe('Auth Resilience', () => {
  test('resend verification enforces cooldown immediately after registration', async ({ request, testApi }) => {
    const unique = `${Date.now()}-${Math.random().toString(16).slice(2, 8)}`;
    const email = `resend-cooldown-${unique}@test.example.com`;
    const password = 'ResendFlow123!';

    let userId = 0;

    try {
      const register = await request.post('/api/auth/register', {
        data: {
          email,
          displayName: `Cooldown ${unique}`,
          password,
          confirmPassword: password,
        },
      });

      expect(register.ok()).toBeTruthy();
      const registerBody = await register.json();
      expect(registerBody.success).toBeTruthy();
      userId = registerBody.user?.id ?? 0;
      expect(userId).toBeGreaterThan(0);

      const resend = await request.post('/api/auth/resend-verification');
      expect(resend.ok()).toBeTruthy();
      const resendBody = await resend.json();
      expect(resendBody.success).toBeFalsy();
      expect(String(resendBody.error || '').toLowerCase()).toContain('wait');
    } finally {
      if (userId > 0) {
        await testApi.deleteUser(userId);
      }
    }
  });
});
