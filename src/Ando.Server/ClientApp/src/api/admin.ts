// =============================================================================
// api/admin.ts
//
// Admin API functions.
// =============================================================================

import api from './client';
import type {
  AdminDashboardDto,
  UserListItemDto,
  UserDetailsDto,
  AdminProjectDto,
  SystemUpdateStatusResponse,
  TriggerSystemUpdateResponse,
  SystemHealthResponse,
} from '@/types';

export async function getAdminDashboard(): Promise<{ dashboard: AdminDashboardDto }> {
  const response = await api.get('/admin/dashboard');
  return { dashboard: response.data };
}

export async function getUsers(): Promise<{ users: UserListItemDto[] }> {
  const response = await api.get('/admin/users');
  return { users: response.data.users ?? [] };
}

export async function getUserDetails(id: number): Promise<{ user: UserDetailsDto }> {
  const response = await api.get(`/admin/users/${id}`);
  return response.data;
}

export async function changeUserRole(
  userId: number,
  isAdmin: boolean
): Promise<{ success: boolean; error?: string }> {
  const response = await api.post(`/admin/users/${userId}/role`, {
    newRole: isAdmin ? 'Admin' : 'User',
  });
  return response.data;
}

export async function lockUser(userId: number): Promise<{ success: boolean; error?: string }> {
  const response = await api.post(`/admin/users/${userId}/lock`);
  return response.data;
}

export async function unlockUser(userId: number): Promise<{ success: boolean; error?: string }> {
  const response = await api.post(`/admin/users/${userId}/unlock`);
  return response.data;
}

export async function deleteUser(userId: number): Promise<{ success: boolean; error?: string }> {
  const response = await api.delete(`/admin/users/${userId}`);
  return response.data;
}

export async function impersonateUser(userId: number): Promise<{ success: boolean; error?: string }> {
  const response = await api.post(`/admin/users/${userId}/impersonate`);
  return response.data;
}

export async function stopImpersonation(): Promise<{ success: boolean; error?: string }> {
  const response = await api.post('/admin/stop-impersonation');
  return response.data;
}

export async function getImpersonationStatus(): Promise<{
  isImpersonating: boolean;
  originalAdminId?: number;
  impersonatedUserId?: number;
}> {
  const response = await api.get('/admin/impersonation-status');
  return response.data;
}

export async function getAdminProjects(): Promise<{ projects: AdminProjectDto[] }> {
  const response = await api.get('/admin/projects');
  const projects = ((response.data.projects ?? []) as Array<{
    id: number;
    repoFullName?: string;
    name?: string;
    ownerEmail?: string;
    totalBuilds?: number;
    buildCount?: number;
    lastBuildStatus?: string | null;
    lastBuildAt?: string | null;
    isConfigured?: boolean;
  }>).map((project) => ({
    id: project.id,
    repoFullName: project.repoFullName ?? project.name ?? '',
    ownerEmail: project.ownerEmail ?? '',
    totalBuilds: project.totalBuilds ?? project.buildCount ?? 0,
    lastBuildStatus: project.lastBuildStatus ?? null,
    lastBuildAt: project.lastBuildAt ?? null,
    isConfigured: project.isConfigured ?? true,
  }));

  return { projects };
}

export async function getSystemUpdateStatus(
  refresh = false
): Promise<SystemUpdateStatusResponse> {
  const response = await api.get('/admin/system-update', {
    params: refresh ? { refresh: true } : undefined,
  });
  return response.data;
}

export async function triggerSystemUpdate(): Promise<TriggerSystemUpdateResponse> {
  const response = await api.post('/admin/system-update');
  return response.data;
}

export async function getSystemHealth(): Promise<SystemHealthResponse> {
  const response = await api.get('/admin/system-health');
  return response.data;
}
