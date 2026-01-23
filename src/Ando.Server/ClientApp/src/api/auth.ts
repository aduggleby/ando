// =============================================================================
// api/auth.ts
//
// Authentication API functions.
// =============================================================================

import api from './client';
import type {
  LoginRequest,
  LoginResponse,
  RegisterRequest,
  RegisterResponse,
  GetMeResponse,
} from '@/types';

export async function login(data: LoginRequest): Promise<LoginResponse> {
  const response = await api.post<LoginResponse>('/auth/login', data);
  return response.data;
}

export async function logout(): Promise<void> {
  await api.post('/auth/logout');
}

export async function register(data: RegisterRequest): Promise<RegisterResponse> {
  const response = await api.post<RegisterResponse>('/auth/register', data);
  return response.data;
}

export async function getMe(): Promise<GetMeResponse> {
  const response = await api.get<GetMeResponse>('/auth/me');
  return response.data;
}

export async function forgotPassword(email: string): Promise<{ success: boolean; message?: string }> {
  const response = await api.post('/auth/forgot-password', { email });
  return response.data;
}

export async function resetPassword(
  email: string,
  token: string,
  password: string
): Promise<{ success: boolean; message?: string }> {
  const response = await api.post('/auth/reset-password', {
    email,
    token,
    password,
    confirmPassword: password,
  });
  return response.data;
}

export async function verifyEmail(userId: string, token: string): Promise<{ success: boolean; message: string }> {
  const response = await api.post('/auth/verify-email', { userId, token });
  return response.data;
}

export async function resendVerification(): Promise<{ success: boolean; error?: string; message?: string }> {
  const response = await api.post('/auth/resend-verification');
  return response.data;
}
