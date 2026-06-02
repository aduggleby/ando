import crypto from 'crypto';
import { test, expect } from '../fixtures/test-fixtures';
import { BuildDetailsPage } from '../pages/builds.page';

const WEBHOOK_SECRET = process.env.GITHUB_WEBHOOK_SECRET || 'test-webhook-secret';

function signWebhookBody(body: string, secret: string): string {
  const hmac = crypto.createHmac('sha256', secret);
  hmac.update(body, 'utf8');
  return `sha256=${hmac.digest('hex')}`;
}

test.describe('Workflow Depth Coverage', () => {
  test('auth lifecycle: register -> verify -> reset password -> login', async ({ request, testApi }) => {
    const unique = `${Date.now()}-${Math.random().toString(16).slice(2, 8)}`;
    const email = `lifecycle-${unique}@test.example.com`;
    const initialPassword = 'InitialPass123!';
    const newPassword = 'ChangedPass123!';

    let userId = 0;

    try {
      const registerResponse = await request.post('/api/auth/register', {
        data: {
          email,
          displayName: `Lifecycle ${unique}`,
          password: initialPassword,
          confirmPassword: initialPassword,
        },
      });

      expect(registerResponse.ok()).toBeTruthy();
      const registerBody = await registerResponse.json();
      expect(registerBody.success).toBeTruthy();
      userId = registerBody.user?.id ?? 0;
      expect(userId).toBeGreaterThan(0);

      const verificationToken = await testApi.getEmailVerificationToken(userId);
      expect(verificationToken.emailVerified).toBeFalsy();
      expect(verificationToken.token).toBeTruthy();

      const verifyResponse = await request.post('/api/auth/verify-email', {
        data: {
          userId: String(userId),
          token: verificationToken.token,
        },
      });

      expect(verifyResponse.ok()).toBeTruthy();
      const verifyBody = await verifyResponse.json();
      expect(verifyBody.success).toBeTruthy();

      const loginBeforeReset = await request.post('/api/auth/login', {
        data: {
          email,
          password: initialPassword,
          rememberMe: true,
        },
      });

      expect(loginBeforeReset.ok()).toBeTruthy();
      expect((await loginBeforeReset.json()).success).toBeTruthy();

      const forgotResponse = await request.post('/api/auth/forgot-password', {
        data: { email },
      });
      expect(forgotResponse.ok()).toBeTruthy();
      expect((await forgotResponse.json()).success).toBeTruthy();

      const resetToken = await testApi.generatePasswordResetToken(userId);
      expect(resetToken.token.length).toBeGreaterThan(20);

      const resetResponse = await request.post('/api/auth/reset-password', {
        data: {
          email,
          token: resetToken.token,
          password: newPassword,
          confirmPassword: newPassword,
        },
      });

      expect(resetResponse.ok()).toBeTruthy();
      expect((await resetResponse.json()).success).toBeTruthy();

      const reusedTokenAttempt = await request.post('/api/auth/reset-password', {
        data: {
          email,
          token: resetToken.token,
          password: 'ReusedTokenPass123!',
          confirmPassword: 'ReusedTokenPass123!',
        },
      });

      expect(reusedTokenAttempt.ok()).toBeTruthy();
      const reusedTokenBody = await reusedTokenAttempt.json();
      expect(reusedTokenBody.success).toBeFalsy();

      const oldPasswordLogin = await request.post('/api/auth/login', {
        data: {
          email,
          password: initialPassword,
          rememberMe: true,
        },
      });

      expect(oldPasswordLogin.ok()).toBeTruthy();
      const oldPasswordBody = await oldPasswordLogin.json();
      expect(oldPasswordBody.success).toBeFalsy();

      const newPasswordLogin = await request.post('/api/auth/login', {
        data: {
          email,
          password: newPassword,
          rememberMe: true,
        },
      });

      expect(newPasswordLogin.ok()).toBeTruthy();
      const newPasswordBody = await newPasswordLogin.json();
      expect(newPasswordBody.success).toBeTruthy();
      expect(newPasswordBody.user?.emailVerified).toBeTruthy();
    } finally {
      if (userId > 0) {
        await testApi.deleteUser(userId);
      }
    }
  });

  test('auth error paths: invalid verify token and invalid reset token are rejected', async ({ request, testApi }) => {
    const unique = `${Date.now()}-${Math.random().toString(16).slice(2, 8)}`;
    const email = `auth-errors-${unique}@test.example.com`;
    const password = 'InvalidFlow123!';

    let userId = 0;

    try {
      const registerResponse = await request.post('/api/auth/register', {
        data: {
          email,
          displayName: `Auth Errors ${unique}`,
          password,
          confirmPassword: password,
        },
      });

      expect(registerResponse.ok()).toBeTruthy();
      const registerBody = await registerResponse.json();
      expect(registerBody.success).toBeTruthy();
      userId = registerBody.user?.id ?? 0;
      expect(userId).toBeGreaterThan(0);

      const invalidVerify = await request.post('/api/auth/verify-email', {
        data: {
          userId: String(userId),
          token: 'definitely-invalid-token',
        },
      });

      expect(invalidVerify.ok()).toBeTruthy();
      const invalidVerifyBody = await invalidVerify.json();
      expect(invalidVerifyBody.success).toBeFalsy();
      expect(String(invalidVerifyBody.message || '').toLowerCase()).toContain('invalid');

      const invalidReset = await request.post('/api/auth/reset-password', {
        data: {
          email,
          token: 'invalid-reset-token',
          password: 'AnotherPass123!',
          confirmPassword: 'AnotherPass123!',
        },
      });

      expect(invalidReset.ok()).toBeTruthy();
      const invalidResetBody = await invalidReset.json();
      expect(invalidResetBody.success).toBeFalsy();
      expect(String(invalidResetBody.error || '').toLowerCase()).toContain('invalid');
    } finally {
      if (userId > 0) {
        await testApi.deleteUser(userId);
      }
    }
  });

  test('admin workflow: role assignment, impersonation start/stop, and status checks', async ({ authedPage, authenticatedUser, testApi }) => {
    const targetUser = await testApi.createUser();

    try {
      await testApi.setUserRole(authenticatedUser.id, 'Admin');
      await authedPage.request.post(`/api/test/users/${authenticatedUser.id}/login`, {
        headers: { 'X-Test-Api-Key': 'test-api-key-for-e2e-tests' },
      });

      const listUsersResponse = await authedPage.request.get('/api/admin/users');
      expect(listUsersResponse.ok()).toBeTruthy();
      const listUsersBody = await listUsersResponse.json();
      expect(Array.isArray(listUsersBody.users)).toBeTruthy();
      expect(listUsersBody.users.some((u: { id: number }) => u.id === targetUser.userId)).toBeTruthy();

      const impersonateResponse = await authedPage.request.post(`/api/admin/users/${targetUser.userId}/impersonate`);
      expect(impersonateResponse.ok()).toBeTruthy();
      const impersonateBody = await impersonateResponse.json();
      expect(impersonateBody.success).toBeTruthy();

      const statusWhileImpersonating = await authedPage.request.get('/api/admin/impersonation-status');
      expect(statusWhileImpersonating.ok()).toBeTruthy();
      const statusBody = await statusWhileImpersonating.json();
      expect(statusBody.isImpersonating).toBeTruthy();
      expect(statusBody.originalUserId).toBe(authenticatedUser.id);

      const meWhileImpersonating = await authedPage.request.get('/api/auth/me');
      expect(meWhileImpersonating.ok()).toBeTruthy();
      const meBody = await meWhileImpersonating.json();
      expect(meBody.isAuthenticated).toBeTruthy();
      expect(meBody.user?.id).toBe(targetUser.userId);

      const stopResponse = await authedPage.request.post('/api/admin/stop-impersonation');
      expect(stopResponse.ok()).toBeTruthy();
      const stopBody = await stopResponse.json();
      expect(stopBody.success).toBeTruthy();

      const statusAfterStop = await authedPage.request.get('/api/admin/impersonation-status');
      expect(statusAfterStop.ok()).toBeTruthy();
      const statusAfterStopBody = await statusAfterStop.json();
      expect(statusAfterStopBody.isImpersonating).toBeFalsy();
    } finally {
      await testApi.setUserRole(authenticatedUser.id, 'User');
      await testApi.deleteUser(targetUser.userId);
    }
  });

  test('webhook contracts: valid signed ping succeeds and signed invalid push payload fails gracefully', async ({ request }) => {
    const pingBody = JSON.stringify({ zen: 'keep-it-logical' });
    const pingSignature = signWebhookBody(pingBody, WEBHOOK_SECRET);

    const pingResponse = await request.post('/webhooks/github', {
      data: pingBody,
      headers: {
        'content-type': 'application/json',
        'x-github-event': 'ping',
        'x-github-delivery': `delivery-${Date.now()}`,
        'x-hub-signature-256': pingSignature,
      },
    });

    expect(pingResponse.status()).toBe(200);
    const pingPayload = await pingResponse.json();
    expect(pingPayload.message).toBe('pong');

    const badPushBody = JSON.stringify({ notRepository: true });
    const badPushSignature = signWebhookBody(badPushBody, WEBHOOK_SECRET);

    const badPushResponse = await request.post('/webhooks/github', {
      data: badPushBody,
      headers: {
        'content-type': 'application/json',
        'x-github-event': 'push',
        'x-github-delivery': `delivery-${Date.now()}-bad`,
        'x-hub-signature-256': badPushSignature,
      },
    });

    expect(badPushResponse.status()).toBe(400);
    const badPushPayload = await badPushResponse.json();
    expect(String(badPushPayload.error || '').toLowerCase()).toContain('invalid payload');
  });

  test('artifact download blocks path traversal storage paths', async ({ authedPage, testApi, testProject, testBuild }) => {
    const artifact = await testApi.addArtifact(testBuild.id, {
      name: 'malicious.txt',
      storagePath: '../../../etc/passwd',
      sizeBytes: 12,
    });

    const response = await authedPage.request.get(`/api/builds/${testBuild.id}/artifacts/${artifact.artifactId}`);
    expect(response.status()).toBe(404);
  });

  test('project/build golden workflow keeps UI and API behavior aligned', async ({ authedPage, testApi, authenticatedUser }) => {
    const project = await testApi.createProject({
      userId: authenticatedUser.id,
      repoName: `workflow-depth-${Date.now()}`,
      branchFilter: 'main,release/*',
      notifyOnFailure: true,
    });

    await testApi.addSecret(project.projectId, 'API_KEY', 'value-123');

    const failedBuild = await testApi.createBuild({
      projectId: project.projectId,
      status: 'Failed',
      commitMessage: 'workflow-depth commit',
      branch: 'main',
    });

    await testApi.addLogEntries(failedBuild.buildId, [
      { type: 'Info', message: 'Workflow started' },
      { type: 'Error', message: 'Workflow failed due to synthetic error' },
    ]);

    await testApi.addArtifact(failedBuild.buildId, {
      name: 'report.txt',
      storagePath: `/tmp/test-artifacts/${failedBuild.buildId}/report.txt`,
      sizeBytes: 2048,
    });

    await authedPage.goto(`/projects/${project.projectId}`);
    await expect(authedPage.locator('body')).toContainText(project.repoFullName);

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(failedBuild.buildId);

    await buildDetails.expectStatus('FAILED');
    await buildDetails.expectLogEntry(/workflow failed due to synthetic error/i);
    await expect.poll(async () => buildDetails.getArtifactCount()).toBeGreaterThan(0);

    await buildDetails.retry();
    await expect(authedPage).toHaveURL(/\/builds\/\d+/);
  });
});
