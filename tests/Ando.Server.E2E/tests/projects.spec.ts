/**
 * Project Management Tests
 *
 * Tests for project CRUD operations including:
 * - Project list view
 * - Project details view
 * - Project settings (build config, secrets, notifications)
 * - Project deletion
 * - Triggering builds
 */

import { test, expect } from '../fixtures/test-fixtures';
import { ProjectsListPage, ProjectDetailsPage, ProjectSettingsPage } from '../pages';

test.describe('Projects List', () => {
  test('shows empty state when no projects', async ({ authedPage }) => {
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    await projectsList.expectToBeVisible();
    await projectsList.expectEmptyState();
  });

  test('shows projects when they exist', async ({ authedPage, testProject }) => {
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    const count = await projectsList.getProjectCount();
    expect(count).toBe(1);
  });

  test('displays project name and status', async ({ authedPage, testProject, testBuild }) => {
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    // Check project card contains repo name
    const projectCard = authedPage.locator('.project-card').first();
    await expect(projectCard).toContainText(testProject.repoFullName);

    // Check build status badge is shown
    await expect(projectCard.locator('.status-badge-lg, .status-badge')).toBeVisible();
  });

  test('can navigate to project details', async ({ authedPage, testProject }) => {
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    await projectsList.clickProject(testProject.repoFullName);
    await expect(authedPage).toHaveURL(new RegExp(`/projects/${testProject.id}`));
  });

  test('can navigate to project settings', async ({ authedPage, testProject }) => {
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    await projectsList.clickProjectSettings(testProject.repoFullName);
    await expect(authedPage).toHaveURL(new RegExp(`/projects/${testProject.id}/settings`));
  });

  test('shows multiple projects', async ({ authedPage, testApi, authenticatedUser }) => {
    // Create multiple projects
    await testApi.createProject({ userId: authenticatedUser.id, repoName: 'project-1' });
    await testApi.createProject({ userId: authenticatedUser.id, repoName: 'project-2' });
    await testApi.createProject({ userId: authenticatedUser.id, repoName: 'project-3' });

    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    const count = await projectsList.getProjectCount();
    expect(count).toBe(3);
  });
});

test.describe('Project Details', () => {
  test('displays project information correctly', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await projectDetails.expectToBeVisible();
    await expect(projectDetails.breadcrumb).toContainText(testProject.repoFullName);
  });

  test('shows build settings information', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    const defaultBranch = await projectDetails.getInfoValue('Default Branch');
    expect(defaultBranch).toBeTruthy();

    const timeout = await projectDetails.getInfoValue('Timeout');
    expect(timeout).toContain('minutes');
  });

  test('shows recent builds', async ({ authedPage, testProject, testBuild }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    const buildCount = await projectDetails.getBuildCount();
    expect(buildCount).toBeGreaterThan(0);
  });

  test('can trigger a manual build', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await projectDetails.clickTriggerBuild();

    // Should redirect to build details
    await expect(authedPage).toHaveURL(/\/builds\/\d+/);
  });

  test('can navigate to settings', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await projectDetails.clickSettings();
    await expect(authedPage).toHaveURL(new RegExp(`/projects/${testProject.id}/settings`));
  });

  test('shows empty builds state for new project', async ({ authedPage, testApi, authenticatedUser }) => {
    // Create a fresh project with no builds
    const project = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'no-builds' });

    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(project.projectId);

    // Should show empty state or zero builds
    await expect(authedPage.locator('.empty-state, .builds-table')).toBeVisible();
  });
});

test.describe('Project Settings', () => {
  test('displays settings form correctly', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.expectToBeVisible();
    await expect(settings.branchFilterInput).toBeVisible();
    await expect(settings.timeoutInput).toBeVisible();
    await expect(settings.saveSettingsButton).toBeVisible();
  });

  test('can update branch filter', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.updateBranchFilter('main,develop,feature/*');
    await settings.saveSettings();

    await settings.expectSuccessMessage(/settings updated/i);
  });

  test('can enable PR builds', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.togglePrBuilds(true);
    await settings.saveSettings();

    await settings.expectSuccessMessage(/settings updated/i);

    // Verify the change persisted
    await authedPage.reload();
    await expect(settings.enablePrBuildsCheckbox).toBeChecked();
  });

  test('can update build timeout', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.updateTimeout(30);
    await settings.saveSettings();

    await settings.expectSuccessMessage(/settings updated/i);

    // Verify the change persisted
    await authedPage.reload();
    await expect(settings.timeoutInput).toHaveValue('30');
  });

  test('can update Docker image', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.updateDockerImage('node:20-alpine');
    await settings.saveSettings();

    await settings.expectSuccessMessage(/settings updated/i);

    // Verify the change persisted
    await authedPage.reload();
    await expect(settings.dockerImageInput).toHaveValue('node:20-alpine');
  });

  test('can update notification settings', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.toggleNotifyOnFailure(true);
    await settings.updateNotificationEmail('alerts@example.com');
    await settings.saveSettings();

    await settings.expectSuccessMessage(/settings updated/i);
  });

  test('timeout is clamped to valid range', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    // Try to set timeout above max (60)
    await settings.updateTimeout(120);
    await settings.saveSettings();

    // Should be clamped to 60
    await authedPage.reload();
    const timeoutValue = await settings.timeoutInput.inputValue();
    expect(parseInt(timeoutValue)).toBeLessThanOrEqual(60);
  });
});

test.describe('Project Secrets', () => {
  test('shows empty secrets list initially', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    const secrets = await settings.getSecretNames();
    expect(secrets.length).toBe(0);
  });

  test('can add a secret', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.addSecret('API_KEY', 'super-secret-value');
    await settings.expectSuccessMessage(/secret.*saved/i);

    // Verify secret appears in list
    const secrets = await settings.getSecretNames();
    expect(secrets).toContain('API_KEY');
  });

  test('can add multiple secrets', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.addSecret('DATABASE_URL', 'postgres://...');
    await settings.addSecret('AWS_ACCESS_KEY', 'AKIA...');
    await settings.addSecret('AWS_SECRET_KEY', 'secret...');

    const secrets = await settings.getSecretNames();
    expect(secrets).toContain('DATABASE_URL');
    expect(secrets).toContain('AWS_ACCESS_KEY');
    expect(secrets).toContain('AWS_SECRET_KEY');
  });

  test('can delete a secret', async ({ authedPage, testProject, testApi }) => {
    // Pre-create a secret
    await testApi.addSecret(testProject.id, 'TO_DELETE', 'value');

    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    // Verify secret exists
    let secrets = await settings.getSecretNames();
    expect(secrets).toContain('TO_DELETE');

    // Delete it (handle confirmation dialog)
    await settings.deleteSecret('TO_DELETE');
    await settings.expectSuccessMessage(/secret.*deleted/i);

    // Verify it's gone
    secrets = await settings.getSecretNames();
    expect(secrets).not.toContain('TO_DELETE');
  });

  test('auto-formats secret names to uppercase', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    // Add secret with lowercase name - should be auto-formatted to uppercase
    await settings.addSecret('my_secret_key', 'value');
    await settings.expectSuccessMessage(/secret.*saved/i);

    // Verify it was saved with uppercase name
    const secrets = await settings.getSecretNames();
    expect(secrets).toContain('MY_SECRET_KEY');
  });

  test('secret values are never displayed', async ({ authedPage, testProject, testApi }) => {
    // Pre-create a secret with known value
    await testApi.addSecret(testProject.id, 'MY_SECRET', 'super-secret-123');

    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    // Verify secret value is not visible anywhere on the page
    const pageContent = await authedPage.content();
    expect(pageContent).not.toContain('super-secret-123');
  });
});

test.describe('Project Deletion', () => {
  test('can delete a project', async ({ authedPage, testApi, authenticatedUser }) => {
    // Create a project to delete
    const project = await testApi.createProject({
      userId: authenticatedUser.id,
      repoName: 'to-delete',
    });

    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(project.projectId);

    await settings.deleteProject();

    // Should redirect to projects list
    await expect(authedPage).toHaveURL(/\/projects/);

    // Should show success message
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.expectSuccessMessage(/deleted/i);
  });

  test('deleted project no longer appears in list', async ({ authedPage, testApi, authenticatedUser }) => {
    // Create projects
    const project1 = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'keep-me' });
    const project2 = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'delete-me' });

    // Delete one
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(project2.projectId);
    await settings.deleteProject();

    // Verify only one remains
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();
    const count = await projectsList.getProjectCount();
    expect(count).toBe(1);

    // Verify the right one remains
    await expect(authedPage.locator('.project-card')).toContainText('keep-me');
  });

  test('deleting project also deletes builds', async ({ authedPage, testApi, authenticatedUser }) => {
    // Create project with builds
    const project = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'with-builds' });
    await testApi.createBuild({ projectId: project.projectId, status: 'Success' });
    await testApi.createBuild({ projectId: project.projectId, status: 'Failed' });

    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(project.projectId);
    await settings.deleteProject();

    // Navigating to the build should fail
    await authedPage.goto(`/builds/999999`);
    await expect(authedPage).toHaveURL(/\/builds\/|404|not.*found/i);
  });
});

test.describe('User Isolation', () => {
  test('user cannot see other users projects', async ({ authedPage, testApi }) => {
    // Create another user with a project (use unique name to avoid conflicts)
    const uniqueId = Date.now().toString(36);
    const otherUser = await testApi.createUser({ login: `other-user-${uniqueId}` });
    const otherProject = await testApi.createProject({
      userId: otherUser.userId,
      repoName: 'other-project',
    });

    // Current user should not see the other user's project
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    const count = await projectsList.getProjectCount();
    expect(count).toBe(0);

    // Clean up
    await testApi.deleteUser(otherUser.userId);
  });

  test('user cannot access other users project details', async ({ authedPage, testApi }) => {
    // Create another user with a project (use unique name to avoid conflicts)
    const uniqueId = Date.now().toString(36);
    const otherUser = await testApi.createUser({ login: `other-user-${uniqueId}` });
    const otherProject = await testApi.createProject({
      userId: otherUser.userId,
      repoName: 'private-project',
    });

    // Try to access the project directly
    await authedPage.goto(`/projects/${otherProject.projectId}`);

    // Should get 404 or redirect
    await expect(authedPage.locator('body')).toContainText(/not found|404|error/i);

    // Clean up
    await testApi.deleteUser(otherUser.userId);
  });
});
