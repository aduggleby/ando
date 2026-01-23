// =============================================================================
// api/dashboard.ts
//
// Dashboard API functions.
// =============================================================================

import api from './client';
import type { DashboardDto } from '@/types';

export async function getDashboard(): Promise<{ dashboard: DashboardDto }> {
  const response = await api.get('/dashboard');
  return response.data;
}
