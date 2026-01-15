/**
 * Test Fixtures
 *
 * Provides isolated test contexts with authenticated users and test data.
 * Each test gets a fresh user to ensure complete isolation.
 */

import { test as base, expect, Page, BrowserContext } from '@playwright/test';
import { TestApi, CreateUserResponse, CreateProjectResponse, CreateBuildResponse } from '../utils/test-api';

// Test API key header name
const TEST_API_KEY = 'test-api-key-for-e2e-tests';

/**
 * Test user context with automatic cleanup.
 */
export interface TestUser {
  id: number;
  login: string;
  email: string;
}

/**
 * Test project context.
 */
export interface TestProject {
  id: number;
  repoFullName: string;
  repoUrl: string;
}

/**
 * Test build context.
 */
export interface TestBuild {
  id: number;
  status: string;
}

/**
 * Extended test fixtures.
 */
export interface TestFixtures {
  /** Test API client */
  testApi: TestApi;

  /** Creates and authenticates a new test user */
  authenticatedUser: TestUser;

  /** Creates a test project for the authenticated user */
  testProject: TestProject;

  /** Creates a test build for the test project */
  testBuild: TestBuild;

  /** Authenticated page (logged in as test user) */
  authedPage: Page;
}

/**
 * Worker-scoped fixtures (shared across tests in a worker).
 */
export interface WorkerFixtures {
  /** Base URL for the test server */
  baseUrl: string;
}

/**
 * Extended Playwright test with custom fixtures.
 */
export const test = base.extend<TestFixtures, WorkerFixtures>({
  // Worker-scoped base URL
  baseUrl: [async ({}, use) => {
    await use(process.env.BASE_URL || 'http://localhost:17100');
  }, { scope: 'worker' }],

  // Test API client
  testApi: async ({ request, baseUrl }, use) => {
    const api = new TestApi(request, baseUrl);
    await use(api);
  },

  // Authenticated user with automatic cleanup
  authenticatedUser: async ({ testApi, context, baseUrl }, use) => {
    // Create a new test user
    const userResponse = await testApi.createUser();

    const testUser: TestUser = {
      id: userResponse.userId,
      login: userResponse.login,
      email: userResponse.email,
    };

    // Log in by calling the login endpoint
    const page = await context.newPage();
    const loginResponse = await page.request.post(`${baseUrl}/api/test/users/${testUser.id}/login`, {
      headers: { 'X-Test-Api-Key': TEST_API_KEY },
    });

    if (!loginResponse.ok()) {
      throw new Error(`Failed to login test user: ${loginResponse.status()}`);
    }

    await page.close();

    // Use the test user
    await use(testUser);

    // Cleanup after test
    try {
      await testApi.deleteUser(testUser.id);
    } catch (error) {
      console.warn(`Failed to cleanup user ${testUser.id}:`, error);
    }
  },

  // Test project for the authenticated user
  testProject: async ({ testApi, authenticatedUser }, use) => {
    const projectResponse = await testApi.createProject({
      userId: authenticatedUser.id,
    });

    const testProject: TestProject = {
      id: projectResponse.projectId,
      repoFullName: projectResponse.repoFullName,
      repoUrl: projectResponse.repoUrl,
    };

    await use(testProject);

    // Cleanup happens via user deletion cascade
  },

  // Test build for the test project
  testBuild: async ({ testApi, testProject }, use) => {
    const buildResponse = await testApi.createBuild({
      projectId: testProject.id,
      status: 'Success',
      commitMessage: 'Test commit for E2E testing',
    });

    const testBuild: TestBuild = {
      id: buildResponse.buildId,
      status: buildResponse.status,
    };

    await use(testBuild);

    // Cleanup happens via project deletion cascade
  },

  // Authenticated page (re-uses the authenticated context)
  authedPage: async ({ context, authenticatedUser, baseUrl }, use) => {
    // Login the user
    const page = await context.newPage();

    const loginResponse = await page.request.post(`${baseUrl}/api/test/users/${authenticatedUser.id}/login`, {
      headers: { 'X-Test-Api-Key': TEST_API_KEY },
    });

    if (!loginResponse.ok()) {
      throw new Error(`Failed to login: ${loginResponse.status()}`);
    }

    await use(page);

    await page.close();
  },
});

/**
 * Re-export expect for convenience.
 */
export { expect };

/**
 * Test with authenticated user and project ready.
 */
export const testWithProject = test.extend<{ projectPage: Page }>({
  projectPage: async ({ authedPage, testProject }, use) => {
    await authedPage.goto(`/projects/${testProject.id}`);
    await use(authedPage);
  },
});

/**
 * Test with authenticated user, project, and build ready.
 */
export const testWithBuild = test.extend<{ buildPage: Page }>({
  buildPage: async ({ authedPage, testBuild }, use) => {
    await authedPage.goto(`/builds/${testBuild.id}`);
    await use(authedPage);
  },
});
