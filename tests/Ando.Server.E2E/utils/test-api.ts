/**
 * Test API Client
 *
 * Provides a typed interface for interacting with the test-only API endpoints.
 * Used by test fixtures to create and manage test data.
 */

import { APIRequestContext } from '@playwright/test';

const TEST_API_KEY = 'test-api-key-for-e2e-tests';

export interface CreateUserResponse {
  userId: number;
  gitHubId: number;
  login: string;
  email: string;
  authToken: string;
}

export interface CreateProjectResponse {
  projectId: number;
  repoFullName: string;
  repoUrl: string;
}

export interface CreateBuildResponse {
  buildId: number;
  status: string;
}

export type BuildStatus = 'Queued' | 'Running' | 'Success' | 'Failed' | 'Cancelled' | 'TimedOut';
export type BuildTrigger = 'Push' | 'PullRequest' | 'Manual';
export type LogEntryType = 'StepStarted' | 'StepCompleted' | 'StepFailed' | 'Info' | 'Warning' | 'Error' | 'Output';

export class TestApi {
  private request: APIRequestContext;
  private baseUrl: string;

  constructor(request: APIRequestContext, baseUrl: string) {
    this.request = request;
    this.baseUrl = baseUrl;
  }

  private get headers() {
    return {
      'X-Test-Api-Key': TEST_API_KEY,
      'Content-Type': 'application/json',
    };
  }

  // -------------------------------------------------------------------------
  // Health Check
  // -------------------------------------------------------------------------

  async healthCheck(): Promise<boolean> {
    const response = await this.request.get(`${this.baseUrl}/api/test/health`, {
      headers: this.headers,
    });
    return response.ok();
  }

  // -------------------------------------------------------------------------
  // User Management
  // -------------------------------------------------------------------------

  async createUser(options?: { login?: string; email?: string }): Promise<CreateUserResponse> {
    const response = await this.request.post(`${this.baseUrl}/api/test/users`, {
      headers: this.headers,
      data: options || {},
    });

    if (!response.ok()) {
      throw new Error(`Failed to create user: ${response.status()} ${await response.text()}`);
    }

    return response.json();
  }

  async deleteUser(userId: number): Promise<void> {
    const response = await this.request.delete(`${this.baseUrl}/api/test/users/${userId}`, {
      headers: this.headers,
    });

    if (!response.ok()) {
      throw new Error(`Failed to delete user: ${response.status()}`);
    }
  }

  // -------------------------------------------------------------------------
  // Project Management
  // -------------------------------------------------------------------------

  async createProject(options: {
    userId: number;
    repoName?: string;
    defaultBranch?: string;
    branchFilter?: string;
    enablePrBuilds?: boolean;
    timeoutMinutes?: number;
    notifyOnFailure?: boolean;
    notificationEmail?: string;
  }): Promise<CreateProjectResponse> {
    const response = await this.request.post(`${this.baseUrl}/api/test/projects`, {
      headers: this.headers,
      data: options,
    });

    if (!response.ok()) {
      throw new Error(`Failed to create project: ${response.status()} ${await response.text()}`);
    }

    return response.json();
  }

  async addSecret(projectId: number, name: string, value: string): Promise<void> {
    const response = await this.request.post(`${this.baseUrl}/api/test/projects/${projectId}/secrets`, {
      headers: this.headers,
      data: { name, value },
    });

    if (!response.ok()) {
      throw new Error(`Failed to add secret: ${response.status()}`);
    }
  }

  // -------------------------------------------------------------------------
  // Build Management
  // -------------------------------------------------------------------------

  async createBuild(options: {
    projectId: number;
    commitSha?: string;
    branch?: string;
    commitMessage?: string;
    commitAuthor?: string;
    status?: BuildStatus;
    trigger?: BuildTrigger;
    pullRequestNumber?: number;
  }): Promise<CreateBuildResponse> {
    const response = await this.request.post(`${this.baseUrl}/api/test/builds`, {
      headers: this.headers,
      data: options,
    });

    if (!response.ok()) {
      throw new Error(`Failed to create build: ${response.status()} ${await response.text()}`);
    }

    return response.json();
  }

  async updateBuild(buildId: number, updates: {
    status?: BuildStatus;
    errorMessage?: string;
    stepsTotal?: number;
    stepsCompleted?: number;
    stepsFailed?: number;
  }): Promise<void> {
    const response = await this.request.patch(`${this.baseUrl}/api/test/builds/${buildId}`, {
      headers: this.headers,
      data: updates,
    });

    if (!response.ok()) {
      throw new Error(`Failed to update build: ${response.status()}`);
    }
  }

  async addLogEntries(buildId: number, entries: Array<{
    type?: LogEntryType;
    message: string;
    stepName?: string;
  }>): Promise<{ lastSequence: number }> {
    const response = await this.request.post(`${this.baseUrl}/api/test/builds/${buildId}/logs`, {
      headers: this.headers,
      data: { entries },
    });

    if (!response.ok()) {
      throw new Error(`Failed to add log entries: ${response.status()}`);
    }

    return response.json();
  }

  async addArtifact(buildId: number, options: {
    name: string;
    storagePath?: string;
    sizeBytes?: number;
  }): Promise<{ artifactId: number }> {
    const response = await this.request.post(`${this.baseUrl}/api/test/builds/${buildId}/artifacts`, {
      headers: this.headers,
      data: options,
    });

    if (!response.ok()) {
      throw new Error(`Failed to add artifact: ${response.status()}`);
    }

    return response.json();
  }

  // -------------------------------------------------------------------------
  // Cleanup
  // -------------------------------------------------------------------------

  async cleanup(options?: { userId?: number; projectId?: number }): Promise<void> {
    const response = await this.request.post(`${this.baseUrl}/api/test/cleanup`, {
      headers: this.headers,
      data: options || {},
    });

    if (!response.ok()) {
      throw new Error(`Failed to cleanup: ${response.status()}`);
    }
  }
}
