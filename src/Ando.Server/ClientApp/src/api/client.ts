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

// Response interceptor for handling errors
api.interceptors.response.use(
  (response) => response,
  (error) => {
    // Handle 401 by redirecting to login
    if (error.response?.status === 401) {
      // Don't redirect if already on auth pages
      if (!window.location.pathname.startsWith('/auth')) {
        window.location.href = '/auth/login';
      }
    }
    return Promise.reject(error);
  }
);

export default api;
