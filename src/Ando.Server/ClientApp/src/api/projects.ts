// =============================================================================
// api/projects.ts
//
// Projects API functions.
// =============================================================================

import api from './client';
import type {
  ProjectListItemDto,
  ProjectDetailsDto,
  ProjectSettingsDto,
  ProjectStatusDto,
  CreateProjectResponse,
  UpdateProjectSettingsRequest,
} from '@/types';

export async function getProjects(): Promise<{ projects: ProjectListItemDto[] }> {
  const response = await api.get('/projects');
  return response.data;
}

export async function getProjectsStatus(
  sortBy?: string,
  direction?: string
): Promise<{ projects: ProjectStatusDto[]; sortField: string; sortDirection: string }> {
  const params = new URLSearchParams();
  if (sortBy) params.append('sortBy', sortBy);
  if (direction) params.append('direction', direction);
  const response = await api.get(`/projects/status?${params}`);
  return response.data;
}

export async function getProject(id: number): Promise<{ project: ProjectDetailsDto }> {
  const response = await api.get(`/projects/${id}`);
  return response.data;
}

export async function getProjectSettings(id: number): Promise<{ settings: ProjectSettingsDto }> {
  const response = await api.get(`/projects/${id}/settings`);
  return response.data;
}

export async function createProject(repoUrl: string): Promise<CreateProjectResponse> {
  const response = await api.post('/projects', { repoUrl });
  return response.data;
}

export async function updateProjectSettings(
  id: number,
  data: UpdateProjectSettingsRequest
): Promise<{ success: boolean; error?: string }> {
  const response = await api.put(`/projects/${id}/settings`, data);
  return response.data;
}

export async function deleteProject(id: number): Promise<{ success: boolean; error?: string }> {
  const response = await api.delete(`/projects/${id}`);
  return response.data;
}

export async function triggerBuild(
  id: number,
  branch?: string
): Promise<{ success: boolean; buildId?: number; error?: string }> {
  const response = await api.post(`/projects/${id}/build`, { branch });
  return response.data;
}

export async function setSecret(
  projectId: number,
  name: string,
  value: string
): Promise<{ success: boolean; error?: string }> {
  const response = await api.post(`/projects/${projectId}/secrets`, { name, value });
  return response.data;
}

export async function deleteSecret(
  projectId: number,
  name: string
): Promise<{ success: boolean; error?: string }> {
  const response = await api.delete(`/projects/${projectId}/secrets/${encodeURIComponent(name)}`);
  return response.data;
}

export async function bulkImportSecrets(
  projectId: number,
  content: string
): Promise<{ success: boolean; importedCount: number; errors?: string[] }> {
  const response = await api.post(`/projects/${projectId}/secrets/bulk`, { content });
  return response.data;
}

export async function refreshSecrets(
  projectId: number
): Promise<{ success: boolean; detectedSecrets: string[]; detectedProfiles: string[] }> {
  const response = await api.post(`/projects/${projectId}/refresh-secrets`);
  return response.data;
}
