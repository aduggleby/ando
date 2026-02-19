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

    await expect.poll(async () => projectsList.getProjectCount()).toBe(1);
  });

  test('displays project name and status', async ({ authedPage, testProject, testBuild }) => {
    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    const projectLink = authedPage.locator('a[href^="/projects/"]').filter({ hasText: testProject.repoFullName }).first();
    await expect(projectLink).toBeVisible();
    await expect(projectLink).toContainText(/success|failed|secrets missing/i);
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
    await testApi.createProject({ userId: authenticatedUser.id, repoName: 'project-1' });
    await testApi.createProject({ userId: authenticatedUser.id, repoName: 'project-2' });
    await testApi.createProject({ userId: authenticatedUser.id, repoName: 'project-3' });

    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    await expect.poll(async () => projectsList.getProjectCount()).toBe(3);
  });
});

test.describe('Project Details', () => {
  test('displays project information correctly', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await projectDetails.expectToBeVisible();
    expect(await projectDetails.getProjectName()).toContain(testProject.repoFullName);
  });

  test('shows project summary stats', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    const totalBuilds = await projectDetails.getStatValue('Total Builds');
    const successRate = await projectDetails.getStatValue('Success Rate');
    expect(totalBuilds).toBeTruthy();
    expect(successRate).toBeTruthy();
  });

  test('shows recent builds', async ({ authedPage, testProject, testBuild }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await expect.poll(async () => projectDetails.getBuildCount()).toBeGreaterThan(0);
  });

  test('can trigger a manual build', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await projectDetails.clickTriggerBuild();
    await expect(authedPage).toHaveURL(/\/builds\/\d+/);
  });

  test('can navigate to settings', async ({ authedPage, testProject }) => {
    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(testProject.id);

    await projectDetails.clickSettings();
    await expect(authedPage).toHaveURL(new RegExp(`/projects/${testProject.id}/settings`));
  });

  test('shows empty builds state for new project', async ({ authedPage, testApi, authenticatedUser }) => {
    const project = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'no-builds' });

    const projectDetails = new ProjectDetailsPage(authedPage);
    await projectDetails.goto(project.projectId);

    await projectDetails.expectEmptyBuildsState();
  });
});

test.describe('Project Settings', () => {
  test('displays settings form correctly', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.expectToBeVisible();
  });
});

test.describe('Project Secrets', () => {
  test('shows empty secrets list initially', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await expect(authedPage.getByText('No secrets configured yet.')).toBeVisible();
  });

  test('can add a secret', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.addSecret('API_KEY', 'super-secret-value');
    await settings.expectSuccessMessage(/secret.*saved/i);
  });

  test('can add multiple secrets', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.addSecret('DATABASE_URL', 'postgres://...');
    await settings.expectSuccessMessage(/secret.*saved/i);
    await settings.addSecret('AWS_ACCESS_KEY', 'AKIA...');
    await settings.expectSuccessMessage(/secret.*saved/i);
    await settings.addSecret('AWS_SECRET_KEY', 'secret...');
    await settings.expectSuccessMessage(/secret.*saved/i);
  });

  test('auto-formats secret names to uppercase', async ({ authedPage, testProject }) => {
    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    await settings.secretNameInput.fill('my_secret_key');
    await expect(settings.secretNameInput).toHaveValue('MY_SECRET_KEY');
  });

  test('secret values are never displayed', async ({ authedPage, testProject, testApi }) => {
    await testApi.addSecret(testProject.id, 'MY_SECRET', 'super-secret-123');

    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(testProject.id);

    const pageContent = await authedPage.content();
    expect(pageContent).not.toContain('super-secret-123');
  });
});

test.describe('Project Deletion', () => {
  test('can delete a project', async ({ authedPage, testApi, authenticatedUser }) => {
    const project = await testApi.createProject({
      userId: authenticatedUser.id,
      repoName: 'to-delete',
    });

    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(project.projectId);

    await settings.deleteProject();

    await expect(authedPage).toHaveURL(/\/projects/);
  });

  test('deleted project no longer appears in list', async ({ authedPage, testApi, authenticatedUser }) => {
    await testApi.createProject({ userId: authenticatedUser.id, repoName: 'keep-me' });
    const project2 = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'delete-me' });

    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(project2.projectId);
    await settings.deleteProject();

    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();
    await expect.poll(async () => projectsList.getProjectCount()).toBe(1);

    await expect(projectsList.projectLinks).toContainText('keep-me');
  });

  test('deleting project also deletes builds', async ({ authedPage, testApi, authenticatedUser }) => {
    const project = await testApi.createProject({ userId: authenticatedUser.id, repoName: 'with-builds' });
    const build = await testApi.createBuild({ projectId: project.projectId, status: 'Success' });

    const settings = new ProjectSettingsPage(authedPage);
    await settings.goto(project.projectId);
    await settings.deleteProject();

    await authedPage.goto(`/builds/${build.buildId}`);
    await expect(authedPage.locator('body')).toContainText(/failed to load build|build not found/i);
  });
});

test.describe('User Isolation', () => {
  test('user cannot see other users projects', async ({ authedPage, testApi }) => {
    const uniqueId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
    const otherUser = await testApi.createUser({ login: `other-user-${uniqueId}` });
    await testApi.createProject({
      userId: otherUser.userId,
      repoName: 'other-project',
    });

    const projectsList = new ProjectsListPage(authedPage);
    await projectsList.goto();

    const count = await projectsList.getProjectCount();
    expect(count).toBe(0);

    await testApi.deleteUser(otherUser.userId);
  });

  test('user cannot access other users project details', async ({ authedPage, testApi }) => {
    const uniqueId = `${Date.now().toString(36)}-${Math.random().toString(36).slice(2, 8)}`;
    const otherUser = await testApi.createUser({ login: `other-user-${uniqueId}` });
    const otherProject = await testApi.createProject({
      userId: otherUser.userId,
      repoName: 'private-project',
    });

    await authedPage.goto(`/projects/${otherProject.projectId}`);
    await expect(authedPage.locator('body')).toContainText(/failed to load project|project not found/i);

    await testApi.deleteUser(otherUser.userId);
  });
});
