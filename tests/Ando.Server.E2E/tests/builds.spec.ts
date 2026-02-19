import { test, expect } from '../fixtures/test-fixtures';
import { BuildDetailsPage, ProjectDetailsPage } from '../pages';

test.describe('Build Details', () => {
  test('displays build information correctly', async ({ authedPage, testBuild, testProject }) => {
    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(testBuild.id);

    await buildDetails.expectToBeVisible();
    await expect(authedPage.locator('body')).toContainText(testProject.repoFullName);
  });

  test('shows commit information', async ({ authedPage, testApi, testProject }) => {
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
    expect(sha).toBe('abc123de');

    const branch = await buildDetails.getBranch();
    expect(branch).toBe('feature/test');
  });
});

test.describe('Build Status States', () => {
  test('displays queued status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Queued' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Queued');
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCannotRetry();
  });

  test('displays running status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Running' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Running');
    await buildDetails.expectCanCancel();
    await buildDetails.expectCannotRetry();
  });

  test('displays success status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Success' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Success');
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCannotRetry();
  });

  test('displays failed status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Failed' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Failed');
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCanRetry();
  });

  test('displays cancelled status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Cancelled' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('Cancelled');
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCanRetry();
  });

  test('displays timed out status correctly', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'TimedOut' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.expectStatus('TimedOut');
    await buildDetails.expectCannotCancel();
    await buildDetails.expectCannotRetry();
  });
});

test.describe('Build Actions', () => {
  test('can cancel a running build', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Running' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.cancel();
    await expect
      .poll(
        async () => {
          await authedPage.reload();
          return buildDetails.getStatus();
        },
        { timeout: 30000 }
      )
      .toBe('CANCELLED');
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
    await expect(authedPage).toHaveURL(/\/builds\/\d+/);
  });

  test('can retry a cancelled build', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Cancelled' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await buildDetails.retry();
    await expect(authedPage).toHaveURL(/\/builds\/\d+/);
  });
});

test.describe('Build Logs', () => {
  test('displays log entries', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Success' });

    await testApi.addLogEntries(build.buildId, [
      { type: 'Info', message: 'Starting build...' },
      { type: 'StepStarted', message: 'Running step: build', stepName: 'build' },
      { type: 'Output', message: 'Compiling source files...', stepName: 'build' },
      { type: 'StepCompleted', message: 'Step completed: build', stepName: 'build' },
    ]);

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await expect.poll(async () => buildDetails.getLogCount()).toBe(4);

    await buildDetails.expectLogEntry('Starting build...');
    await buildDetails.expectLogEntry('Compiling source files...');
  });

  test('shows no logs placeholder when build has no logs', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Success' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await expect(authedPage.locator('body')).toContainText(/no logs available/i);
  });
});

test.describe('Build Artifacts', () => {
  test('displays artifacts when present', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Success' });

    await testApi.addArtifact(build.buildId, {
      name: 'app.zip',
      sizeBytes: 1024 * 1024,
    });

    await testApi.addArtifact(build.buildId, {
      name: 'coverage-report.html',
      sizeBytes: 50 * 1024,
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await expect.poll(async () => buildDetails.getArtifactCount()).toBe(2);

    await expect(buildDetails.artifactsSection).toContainText('app.zip');
    await expect(buildDetails.artifactsSection).toContainText('coverage-report.html');
  });

  test('shows artifact sizes', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Success' });

    await testApi.addArtifact(build.buildId, {
      name: 'large-file.bin',
      sizeBytes: 10 * 1024 * 1024,
    });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    await expect(buildDetails.artifactsSection).toContainText(/MB|KB|B|GB/);
  });

  test('no artifacts section when build has none', async ({ authedPage, testApi, testProject }) => {
    const build = await testApi.createBuild({ projectId: testProject.id, status: 'Success' });

    const buildDetails = new BuildDetailsPage(authedPage);
    await buildDetails.goto(build.buildId);

    const artifactCount = await buildDetails.getArtifactCount();
    expect(artifactCount).toBe(0);
  });
});

test.describe('Build Triggering', () => {
  test('can trigger build from project details', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await projectDetails.clickTriggerBuild();
    await expect(authedPage).toHaveURL(/\/builds\/\d+/);

    const buildDetails = new BuildDetailsPage(authedPage);
    const triggeredBy = await buildDetails.getTriggeredBy();
    expect(triggeredBy).toBeTruthy();
  });
});

test.describe('User Isolation', () => {
  test('cannot view other users builds', async ({ authedPage, testApi }) => {
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

    await authedPage.goto(`/builds/${otherBuild.buildId}`);
    await expect(authedPage.locator('body')).toContainText(/failed to load build|build not found/i);

    await testApi.deleteUser(otherUser.userId);
  });
});
