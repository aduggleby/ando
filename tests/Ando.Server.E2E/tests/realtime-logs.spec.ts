/**
 * Real-time Log Streaming Tests
 *
 * Tests for the SignalR-based live log streaming including:
 * - Connection establishment
 * - Live log updates
 * - Build completion notification
 * - Auto-scroll behavior
 * - Reconnection handling
 */

import { test, expect } from '../fixtures/test-fixtures';
import { BuildDetailsPage } from '../pages';

test.describe('Real-time Log Streaming', () => {
  test.describe('Live Indicator', () => {
    test('shows live indicator for queued builds', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Queued',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await buildDetails.expectLive();
    });

    test('shows live indicator for running builds', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await buildDetails.expectLive();
    });

    test('hides live indicator for completed builds', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Success',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await buildDetails.expectNotLive();
    });
  });

  test.describe('SignalR Connection', () => {
    test('loads SignalR script for live builds', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      // Listen for script loading
      const signalRLoaded = authedPage.waitForResponse(resp =>
        resp.url().includes('signalr') && resp.status() === 200
      );

      await authedPage.goto(`/builds/${build.buildId}`);

      // SignalR script should be loaded
      await expect(signalRLoaded).resolves.toBeTruthy();
    });

    test('does not load SignalR for completed builds', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Success',
      });

      let signalRRequested = false;
      authedPage.on('request', req => {
        if (req.url().includes('signalr')) {
          signalRRequested = true;
        }
      });

      await authedPage.goto(`/builds/${build.buildId}`);

      // Wait a bit to ensure no SignalR request is made
      await authedPage.waitForTimeout(1000);

      // For completed builds, SignalR should not be initialized
      // (though the script might still be in the page, it won't connect)
      expect(signalRRequested).toBe(false);
    });
  });

  test.describe('Live Log Updates', () => {
    test('displays logs added after page load', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      // Initially no logs
      const initialCount = await buildDetails.getLogCount();
      expect(initialCount).toBe(0);

      // Add logs via API (simulating build progress)
      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'Build started' },
        { type: 'Output', message: 'Downloading dependencies...' },
      ]);

      // Wait for logs to appear (via SignalR or polling)
      await buildDetails.waitForLogEntry('Build started', 10000);
      await buildDetails.waitForLogEntry('Downloading dependencies...', 5000);
    });

    test('displays logs in correct order', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      // Add multiple log entries
      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'Step 1' },
        { type: 'Info', message: 'Step 2' },
        { type: 'Info', message: 'Step 3' },
      ]);

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      // Get all log messages
      const logTexts = await authedPage.locator('.log-message').allTextContents();

      // Verify order
      const step1Index = logTexts.findIndex(t => t.includes('Step 1'));
      const step2Index = logTexts.findIndex(t => t.includes('Step 2'));
      const step3Index = logTexts.findIndex(t => t.includes('Step 3'));

      expect(step1Index).toBeLessThan(step2Index);
      expect(step2Index).toBeLessThan(step3Index);
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

      // Complete the build
      await testApi.updateBuild(build.buildId, { status: 'Success' });

      // Wait for status update
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

      await buildDetails.expectLive();

      // Complete the build
      await testApi.updateBuild(build.buildId, { status: 'Failed' });

      // Reload to see updated state
      await authedPage.reload();
      await buildDetails.expectNotLive();
    });

    test('action buttons update when build completes', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      // Initially can cancel but not retry
      await buildDetails.expectCanCancel();
      await buildDetails.expectCannotRetry();

      // Complete the build as failed
      await testApi.updateBuild(build.buildId, {
        status: 'Failed',
        errorMessage: 'Build failed',
      });

      // Reload to see updated state
      await authedPage.reload();

      // Now can retry but not cancel
      await buildDetails.expectCannotCancel();
      await buildDetails.expectCanRetry();
    });
  });

  test.describe('Auto-scroll Behavior', () => {
    test('scroll toggle button is visible for live builds', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await expect(buildDetails.scrollToggle).toBeVisible();
    });

    test('scroll toggle button is hidden for completed builds', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Success',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      await expect(buildDetails.scrollToggle).not.toBeVisible();
    });

    test('can toggle auto-scroll', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      // Initial state should be "On"
      await expect(buildDetails.scrollToggle).toContainText(/on/i);

      // Toggle off
      await buildDetails.toggleAutoScroll();
      await expect(buildDetails.scrollToggle).toContainText(/off/i);

      // Toggle back on
      await buildDetails.toggleAutoScroll();
      await expect(buildDetails.scrollToggle).toContainText(/on/i);
    });
  });

  test.describe('Log Catch-up', () => {
    test('fetches missed logs on page load', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      // Add logs before page load
      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'Pre-existing log 1' },
        { type: 'Info', message: 'Pre-existing log 2' },
        { type: 'Info', message: 'Pre-existing log 3' },
      ]);

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      // All pre-existing logs should be visible
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

      // Call the logs API directly
      const response = await authedPage.request.get(`/builds/${build.buildId}/logs?afterSequence=0`);
      expect(response.ok()).toBeTruthy();

      const data = await response.json();
      expect(data.logs.length).toBe(2);
      expect(data.status).toBe('Running');
      expect(data.isComplete).toBe(false);
    });
  });

  test.describe('Placeholder States', () => {
    test('shows waiting message when no logs yet', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Queued',
      });

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      // Should show placeholder
      await expect(authedPage.locator('.log-placeholder')).toBeVisible();
      await expect(authedPage.locator('.log-placeholder')).toContainText(/waiting.*logs/i);
    });

    test('placeholder disappears when logs arrive', async ({ authedPage, testApi, testProject }) => {
      const build = await testApi.createBuild({
        projectId: testProject.id,
        status: 'Running',
      });

      // Add a log entry
      await testApi.addLogEntries(build.buildId, [
        { type: 'Info', message: 'First log entry' },
      ]);

      const buildDetails = new BuildDetailsPage(authedPage);
      await buildDetails.goto(build.buildId);

      // Placeholder should not be visible
      await expect(authedPage.locator('.log-placeholder')).not.toBeVisible();

      // Log should be visible
      await buildDetails.expectLogEntry('First log entry');
    });
  });
});
