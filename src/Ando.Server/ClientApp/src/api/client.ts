// =============================================================================
// api/client.ts
//
// Axios client instance configured for the FastEndpoints API.
// Handles base URL, credentials, and error responses.
// =============================================================================

import axios from 'axios';

const api = axios.create({
  baseURL: '/api',
  withCredentials: true, // Required for cookie-based auth
  headers: {
    'Content-Type': 'application/json',
  },
});

// Callback for handling auth loss. Set by AuthContext to avoid hard redirects
// (window.location.href) that lose React state and cause full page reloads.
let onAuthLost: (() => void) | null = null;

export function setOnAuthLost(callback: (() => void) | null) {
  onAuthLost = callback;
}

// Response interceptor for handling 401 errors.
// Instead of hard-redirecting (which caused full page reloads and lost state),
// we delegate to AuthContext which handles the redirect via React Router.
api.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      // Don't trigger auth loss for auth endpoints (login, register, etc.)
      // or for the /auth/me check (which returns 200 with isAuthenticated: false).
      const url = error.config?.url || '';
      if (!url.startsWith('/auth/') && !url.startsWith('auth/')) {
        onAuthLost?.();
      }
    }
    return Promise.reject(error);
  }
);

export default api;
