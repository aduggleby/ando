import api from './client';

export interface AppVersionResponse {
  version: string;
}

export async function getAppVersion(): Promise<AppVersionResponse> {
  const response = await api.get('/app/version');
  return response.data;
}
