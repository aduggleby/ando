// =============================================================================
// api/auth.ts
//
// Authentication API functions.
//
// All functions catch HTTP error responses and extract meaningful error messages
// instead of throwing, so callers always receive a typed response object with
// success: false and a user-friendly error string.
// =============================================================================

import axios from 'axios';
import api from './client';
import type {
  LoginRequest,
  LoginResponse,
  RegisterRequest,
  RegisterResponse,
  GetMeResponse,
} from '@/types';

// Extracts a user-friendly error message from an axios error response.
// Handles FastEndpoints validation errors ({ errors: { field: [...] } }),
// standard error responses ({ error: "..." }), and generic messages.
function extractErrorMessage(error: unknown, fallback: string): string {
  if (axios.isAxiosError(error) && error.response?.data) {
    const data = error.response.data;

    // FastEndpoints validation error format: { errors: { field: ["msg", ...] } }
    if (data.errors && typeof data.errors === 'object') {
      const messages = Object.values(data.errors).flat();
      if (messages.length > 0) return messages[0] as string;
    }

    // Our own error response format: { error: "message" }
    if (typeof data.error === 'string') return data.error;

    // Generic message field (e.g. ASP.NET ProblemDetails)
    if (typeof data.message === 'string') return data.message;

    // FastEndpoints may also use title
    if (typeof data.title === 'string') return data.title;
  }
  return fallback;
}

export async function login(data: LoginRequest): Promise<LoginResponse> {
  try {
    const response = await api.post<LoginResponse>('/auth/login', data);
    return response.data;
  } catch (error) {
    return { success: false, error: extractErrorMessage(error, 'Login failed. Please try again.'), user: null };
  }
}

export async function devLogin(): Promise<LoginResponse> {
  try {
    const response = await api.post<LoginResponse>('/auth/dev-login');
    return response.data;
  } catch (error) {
    return { success: false, error: extractErrorMessage(error, 'Development login failed.'), user: null };
  }
}

export async function logout(): Promise<void> {
  await api.post('/auth/logout');
}

export async function register(data: RegisterRequest): Promise<RegisterResponse> {
  try {
    const response = await api.post<RegisterResponse>('/auth/register', data);
    return response.data;
  } catch (error) {
    return { success: false, error: extractErrorMessage(error, 'Registration failed. Please try again.'), isFirstUser: false, user: null };
  }
}

export async function getMe(): Promise<GetMeResponse> {
  const response = await api.get<GetMeResponse>('/auth/me');
  return response.data;
}

export async function forgotPassword(email: string): Promise<{ success: boolean; message?: string }> {
  try {
    const response = await api.post('/auth/forgot-password', { email });
    return response.data;
  } catch (error) {
    return { success: false, message: extractErrorMessage(error, 'Failed to send reset email. Please try again.') };
  }
}

export async function resetPassword(
  email: string,
  token: string,
  password: string
): Promise<{ success: boolean; message?: string }> {
  try {
    const response = await api.post('/auth/reset-password', {
      email,
      token,
      password,
      confirmPassword: password,
    });
    return response.data;
  } catch (error) {
    return { success: false, message: extractErrorMessage(error, 'Failed to reset password. Please try again.') };
  }
}

export async function verifyEmail(userId: string, token: string): Promise<{ success: boolean; message: string }> {
  try {
    const response = await api.post('/auth/verify-email', { userId, token });
    return response.data;
  } catch (error) {
    return { success: false, message: extractErrorMessage(error, 'Email verification failed. Please try again.') };
  }
}

export async function resendVerification(): Promise<{ success: boolean; error?: string; message?: string }> {
  try {
    const response = await api.post('/auth/resend-verification');
    return response.data;
  } catch (error) {
    return { success: false, error: extractErrorMessage(error, 'Failed to resend verification email. Please try again.') };
  }
}
