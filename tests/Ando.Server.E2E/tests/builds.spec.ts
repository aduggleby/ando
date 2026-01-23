/**
 * Build Management Tests
 *
 * Tests for build operations including:
 * - Build details display
 * - Build status states (queued, running, success, failed, cancelled, timed out)
 * - Cancel and retry actions
 * - Artifact display and download
 * - Build logs display
 */

import { test, expect } from '../fixtures/test-fixtures';
import { BuildDetailsPage, ProjectDetailsPage } from '../pages';

test.describe('Build Details', () => {
  test('displays build information correctly', async ({ authedPage, testBuild, testProject }) => {
    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(testBuild.id);

    await buildDetails.expectToBeVisible();
    await expect(buildDetails.breadcrumb).toContainText(testProject.repoFullName);
  });

  test('shows commit information', async ({ authedPage, testApi, testProject }) => {
    // Create a build with specific commit info
    const build = await testApi.createBuild({
      projectId: testProject.id,
      commitSha: 'abc123def456789012345678901234567890abcd',
      branch: 'feature/test',
      commitMessage: 'Add new feature',
      commitAuthor: 'Test Author',
      status: 'Success',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    const sha = await buildDetails.getCommitSha();
    expect(sha).toBe('abc123de'); // Short SHA

    const branch = await buildDetails.getBranch();
    expect(branch).toBe('feature/test');
  });

  test('shows build trigger type', async ({ authedPage, testApi, testProject }) => {
    // Create a manual build
    const build = await testApi.createBuild({
      projectId: testProject.id,
      trigger: 'Manual',
      status: 'Success',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    const trigger = await buildDetails.getTrigger();
    expect(trigger).toBe('Manual');
  });

  test('shows PR number for PR builds', async ({ authedPage, testApi, testProject }) => {
    // Create a PR build
    const build = await testApi.createBuild({
      projectId: testProject.id,
      trigger: 'PullRequest',
      pullRequestNumber: 42,
      branch: 'feature/pr-test',
      status: 'Success',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    // Should show PR badge
    await expect(authedPage.locator('.pr-badge, .pr-link')).toContainText('42');
  });
});

test.describe('Build Status States', () => {
  test('displays queued status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Queued',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Queued');
    await buildDetails.expectLive();
    await buildDetails.expectCanCancel();
    await buildDetails.expectCannotRetry();
  });

  test('displays running status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Running',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Running');
    await buildDetails.expectLive();
    await buildDetails.expectCanCancel();
    await buildDetails.expectCannotRetry();
  });

  test('displays success status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Success',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Success');
    await buildDetails.expectNotLive();
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCannotRetry();
  });

  test('displays failed status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Failed',
    });

    await testApi.updateBuild(build.buildId, {
      errorMessage: 'Build failed: test error',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Failed');
    await buildDetails.expectNotLive();
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCanRetry();
    await buildDetails.expectErrorMessage('Build failed: test error');
  });

  test('displays cancelled status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Cancelled',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Cancelled');
    await buildDetails.expectNotLive();
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCanRetry();
  });

  test('displays timed out status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'TimedOut',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('TimedOut');
    await buildDetails.expectNotLive();
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCanRetry();
  });
});

test.describe('Build Actions', () => {
  test('can cancel a queued build', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Queued',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.cancel();

    await buildDetails.expectSuccessMessage(/cancelled/i);
    await buildDetails.expectStatus('Cancelled');
  });

  test('can cancel a running build', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Running',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.cancel();

    await buildDetails.expectSuccessMessage(/cancelled/i);
  });

  test('can retry a failed build', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Failed',
      commitSha: 'abc123def456789012345678901234567890abcd',
      branch: 'main',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.retry();

    // Should redirect to new build
    await expect(authedPage).toHaveURL(/\/builds\/\d+/);
    await buildDetails.expectSuccessMessage(/queued.*retry/i);
  });

  test('can retry a cancelled build', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Cancelled',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.retry();

    await expect(authedPage).toHaveURL(/\/builds\/\d+/);
  });

  test('can retry a timed out build', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'TimedOut',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.retry();

    await expect(authedPage).toHaveURL(/\/builds\/\d+/);
  });
});

test.describe('Build Logs', () => {
  test('displays log entries', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Success',
    });

    await testApi.addLogEntries(build.buildId, [
      { type: 'Info', message: 'Starting build...' },
      { type: 'StepStarted', message: 'Running step: build', stepName: 'build' },
      { type: 'Output', message: 'Compiling source files...', stepName: 'build' },
      { type: 'StepCompleted', message: 'Step completed: build', stepName: 'build' },
    ]);

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    const logCount = await buildDetails.getLogCount();
    expect(logCount).toBe(4);

    await buildDetails.expectLogEntry('Starting build...');
    await buildDetails.expectLogEntry('Compiling source files...');
  });

  test('displays different log types with styling', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Failed',
    });

    await testApi.addLogEntries(build.buildId, [
      { type: 'StepStarted', message: 'Running tests', stepName: 'test' },
      { type: 'Warning', message: 'Deprecated API used' },
      { type: 'Error', message: 'Test failed: assertion error' },
      { type: 'StepFailed', message: 'Step failed: test', stepName: 'test' },
    ]);

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    // Check log entries are visible
    await buildDetails.expectLogEntry('Running tests');
    await buildDetails.expectLogEntry('Deprecated API used');
    await buildDetails.expectLogEntry('Test failed');
  });

  test('shows step names in log entries', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Success',
    });

    await testApi.addLogEntries(build.buildId, [
      { type: 'Output', message: 'Building project...', stepName: 'build' },
      { type: 'Output', message: 'Running tests...', stepName: 'test' },
    ]);

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    // Check step names are shown
    await expect(authedPage.locator('.log-step').first()).toContainText('[build]');
    await expect(authedPage.locator('.log-step').last()).toContainText('[test]');
  });
});

test.describe('Build Artifacts', () => {
  test('displays artifacts when present', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Success',
    });

    await testApi.addArtifact(build.buildId, {
      name: 'app.zip',
      sizeBytes: 1024 * 1024, // 1 MB
    });

    await testApi.addArtifact(build.buildId, {
      name: 'coverage-report.html',
      sizeBytes: 50 * 1024, // 50 KB
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    const artifactCount = await buildDetails.getArtifactCount();
    expect(artifactCount).toBe(2);

    const names = await buildDetails.getArtifactNames();
    expect(names).toContain('app.zip');
    expect(names).toContain('coverage-report.html');
  });

  test('shows artifact sizes', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Success',
    });

    await testApi.addArtifact(build.buildId, {
      name: 'large-file.bin',
      sizeBytes: 10 * 1024 * 1024, // 10 MB
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    // Check that size is displayed
    await expect(authedPage.locator('.artifact-size')).toContainText('MB');
  });

  test('no artifacts section when build has none', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Success',
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    const artifactCount = await buildDetails.getArtifactCount();
    expect(artifactCount).toBe(0);
  });
});

test.describe('Build Steps Progress', () => {
  test('displays step progress', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Running',
    });

    await testApi.updateBuild(build.buildId, {
      stepsTotal: 5,
      stepsCompleted: 3,
      stepsFailed: 0,
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    // Check steps info is displayed
    await expect(authedPage.locator('#steps-count, .steps-progress-count'))
      .toContainText('3 / 5');
  });

  test('displays failed step count', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Failed',
    });

    await testApi.updateBuild(build.buildId, {
      stepsTotal: 5,
      stepsCompleted: 3,
      stepsFailed: 2,
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    // Check failed count is shown
    await expect(authedPage.locator('.text-error, .failed-count')).toContainText('2 failed');
  });
});

test.describe('Build Triggering', () => {
  test('can trigger build from project details', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await projectDetails.clickTriggerBuild();

    // Should navigate to the new build
    await expect(authedPage).toHaveURL(/\/builds\/\d+/);

    const buildDetails = new BuildDetailsPage(authedPage);
    const trigger = await buildDetails.getTrigger();
    expect(trigger).toBe('Manual');
  });
});

test.describe('User Isolation', () => {
  test('cannot view other users builds', async ({ authedPage, testApi }) => {
    // Create another user with a project and build (use unique name to avoid conflicts)
    const uniqueId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
    const otherUser = await testApi.createUser({ login: `build-owner-${uniqueId}` });
    const otherProject = await testApi.createProject({
      userId: otherUser.userId,
      repoName: 'private-repo',
    });
    const otherBuild = await testApi.createBuild({
      projectId: otherProject.projectId,
      status: 'Success',
    });

    // Try to access the build
    await authedPage.goto(`/builds/${otherBuild.buildId}`);

    // Should get 404
    await expect(authedPage.locator('body')).toContainText(/not found|404/i);

    // Clean up
    await testApi.deleteUser(otherUser.userId);
  });
});
