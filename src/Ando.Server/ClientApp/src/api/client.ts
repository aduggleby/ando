// =============================================================================
// api/client.ts
//
// Axios client instance configured for the FastEndpoints API.
// Handles base URL, credentials, error responses, and auth diagnostics.
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
    const status = error.response?.status;
    const url = error.config?.url || '';
    const method = error.config?.method?.toUpperCase() || '?';

    // Log all non-2xx responses for auth diagnostics.
    if (status === 401 || status === 403) {
      console.warn(
        `[Auth] ${method} ${url} → ${status}`,
        {
          status,
          url,
          responseData: error.response?.data,
          withCredentials: error.config?.withCredentials,
          hasHeaders: !!error.config?.headers,
        }
      );
    }

    if (status === 401) {
      // Don't trigger auth loss for auth endpoints (login, register, etc.)
      // or for the /auth/me check (which returns 200 with isAuthenticated: false).
      if (!url.startsWith('/auth/') && !url.startsWith('auth/')) {
        console.warn(
          `[Auth] Session lost — received 401 for ${method} ${url}. Clearing auth state.`
        );
        onAuthLost?.();
      }
    }
    return Promise.reject(error);
  }
);

export default api;
