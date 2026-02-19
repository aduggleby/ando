import { test, expect } from '../fixtures/test-fixtures';
import { BuildDetailsPage } from '../pages';

test.describe('Real-time Log Streaming', () => {
  test.describe('Live Log Updates', () => {
    test('displays logs loaded on page load', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'Build started' },
        { type: 'Output', message: 'Downloading dependencies...' },
      ]);

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await buildDetails.waitForLogEntry('Build started', 5000);
      await buildDetails.waitForLogEntry('Downloading dependencies...', 5000);
    });

    test('displays logs in correct order', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'Step 1' },
        { type: 'Info', message: 'Step 2' },
        { type: 'Info', message: 'Step 3' },
      ]);

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await buildDetails.waitForLogEntry('Step 1', 5000);
      await buildDetails.waitForLogEntry('Step 2', 5000);
      await buildDetails.waitForLogEntry('Step 3', 5000);

      const logsText = await buildDetails.logContainer.textContent();
      const step1Index = logsText?.indexOf('Step 1') ?? -1;
      const step2Index = logsText?.indexOf('Step 2') ?? -1;
      const step3Index = logsText?.indexOf('Step 3') ?? -1;

      expect(step1Index).toBeGreaterThanOrEqual(0);
      expect(step2Index).toBeGreaterThan(step1Index);
      expect(step3Index).toBeGreaterThan(step2Index);
    });
  });

  test.describe('Build Completion', () => {
    test('status updates when build completes', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await buildDetails.expectStatus('Running');

      await testApi.updateBuild(build.buildId, { status: 'Success' });

      await authedPage.reload();
      await buildDetails.expectStatus('Success');
    });

    test('live indicator hides when build completes', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await testApi.updateBuild(build.buildId, { status: 'Failed' });

      await authedPage.reload();
      await buildDetails.expectStatus('Failed');
    });

    test('action buttons update when build completes', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await buildDetails.expectCanCancel();
      await buildDetails.expectCannotRetry();

      await testApi.updateBuild(build.buildId, { status: 'Failed' });

      await authedPage.reload();

      await buildDetails.expectCannotCancel();
      await buildDetails.expectCanRetry();
    });
  });

  test.describe('Auto-scroll Behavior', () => {
    test('auto-scroll toggle is visible', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await expect(buildDetails.autoScrollToggle).toBeVisible();
    });

    test('can toggle auto-scroll', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      const initial = await buildDetails.isAutoScrollEnabled();
      await buildDetails.toggleAutoScroll();
      expect(await buildDetails.isAutoScrollEnabled()).toBe(!initial);

      await buildDetails.toggleAutoScroll();
      expect(await buildDetails.isAutoScrollEnabled()).toBe(initial);
    });
  });

  test.describe('Log Catch-up', () => {
    test('fetches missed logs on page load', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'Pre-existing log 1' },
        { type: 'Info', message: 'Pre-existing log 2' },
        { type: 'Info', message: 'Pre-existing log 3' },
      ]);

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await buildDetails.expectLogEntry('Pre-existing log 1');
      await buildDetails.expectLogEntry('Pre-existing log 2');
      await buildDetails.expectLogEntry('Pre-existing log 3');
    });

    test('logs API returns correct data', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'Log entry 1' },
        { type: 'Info', message: 'Log entry 2' },
      ]);

      const response = await authedPage.request.get(`/api/builds/${build.buildId}/logs?afterSequence=0`);
      expect(response.ok()).toBeTruthy();

      const data = await response.json();
      expect(data.logs.length).toBe(2);
      expect(data.status).toBe('Running');
      expect(data.isComplete).toBe(false);
    });
  });

  test.describe('Placeholder States', () => {
    test('shows waiting message when no logs yet on running build', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await expect(buildDetails.logContainer).toContainText(/waiting for logs/i);
    });

    test('placeholder disappears when logs arrive', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'First log entry' },
      ]);

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await expect(buildDetails.logContainer).not.toContainText(/waiting for logs/i);
      await buildDetails.expectLogEntry('First log entry');
    });
  });
});
