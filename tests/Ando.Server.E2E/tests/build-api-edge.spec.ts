import { test, expect } from '../fixtures/test-fixtures';

test.describe('Build API Edge Contracts', () => {
  test('cancel and retry reject builds in invalid states', async ({ authedPage, testApi, testProject }) => {
    const successBuild = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Success',
    });

    const cancelResponse = await authedPage.request.post(`/api/builds/${successBuild.buildId}/cancel`);
    expect(cancelResponse.ok()).toBeTruthy();
    const cancelBody = await cancelResponse.json();
    expect(cancelBody.success).toBeFalsy();
    expect(String(cancelBody.error || '').toLowerCase()).toContain('cannot be cancelled');

    const retryResponse = await authedPage.request.post(`/api/builds/${successBuild.buildId}/retry`);
    expect(retryResponse.ok()).toBeTruthy();
    const retryBody = await retryResponse.json();
    expect(retryBody.success).toBeFalsy();
    expect(String(retryBody.error || '').toLowerCase()).toContain('cannot be retried');
  });

  test('logs endpoint honors afterSequence catch-up contract', async ({ authedPage, testApi, testProject }) => {
    const runningBuild = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Running',
    });

    await testApi.addLogEntries(runningBuild.buildId, [
      { type: 'Info', message: 'line-1' },
      { type: 'Info', message: 'line-2' },
      { type: 'Info', message: 'line-3' },
    ]);

    const allLogsResponse = await authedPage.request.get(`/api/builds/${runningBuild.buildId}/logs`);
    expect(allLogsResponse.ok()).toBeTruthy();
    const allLogsBody = await allLogsResponse.json();
    expect(allLogsBody.logs.length).toBe(3);

    const afterSequence = allLogsBody.logs[0].sequence;
    const deltaLogsResponse = await authedPage.request.get(
      `/api/builds/${runningBuild.buildId}/logs?afterSequence=${afterSequence}`
    );

    expect(deltaLogsResponse.ok()).toBeTruthy();
    const deltaLogsBody = await deltaLogsResponse.json();
    expect(deltaLogsBody.logs.length).toBe(2);
    expect(deltaLogsBody.logs.every((log: { sequence: number }) => log.sequence > afterSequence)).toBeTruthy();
    expect(deltaLogsBody.status).toBe('Running');
    expect(deltaLogsBody.isComplete).toBeFalsy();
  });
});
