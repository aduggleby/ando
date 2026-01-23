// =============================================================================
// api/builds.ts
//
// Builds API functions.
// =============================================================================

import api from './client';
import type { BuildDetailsDto, LogEntry } from '@/types';

export async function getBuild(id: number): Promise<{ build: BuildDetailsDto }> {
  const response = await api.get(`/builds/${id}`);
  return response.data;
}

export async function getBuildLogs(
  id: number,
  afterSequence: number = 0
): Promise<{ logs: LogEntry[]; status: string; isComplete: boolean }> {
  const response = await api.get(`/builds/${id}/logs?afterSequence=${afterSequence}`);
  return response.data;
}

export async function cancelBuild(id: number): Promise<{ success: boolean; error?: string }> {
  const response = await api.post(`/builds/${id}/cancel`);
  return response.data;
}

export async function retryBuild(id: number): Promise<{ success: boolean; buildId?: number; error?: string }> {
  const response = await api.post(`/builds/${id}/retry`);
  return response.data;
}

export function getArtifactDownloadUrl(buildId: number, artifactId: number): string {
  return `/api/builds/${buildId}/artifacts/${artifactId}`;
}
