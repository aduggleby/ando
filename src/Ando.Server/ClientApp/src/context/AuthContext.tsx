// =============================================================================
// context/AuthContext.tsx
//
// React context for managing authentication state across the application.
//
// Handles login/logout state, session restoration via getMe(), and coordinates
// with the axios interceptor to handle 401 responses without hard page reloads.
// =============================================================================

import { createContext, useContext, useEffect, useState, useCallback, type ReactNode } from 'react';
import type { UserDto } from '@/types';
import * as authApi from '@/api/auth';
import { setOnAuthLost } from '@/api/client';

interface ExtendedUser extends UserDto {
  impersonating?: boolean;
}

interface AuthContextType {
  user: ExtendedUser | null;
  isLoading: boolean;
  isAuthenticated: boolean;
  login: (email: string, password: string, rememberMe?: boolean) => Promise<{ success: boolean; error?: string }>;
  devLogin: () => Promise<{ success: boolean; error?: string }>;
  logout: () => Promise<void>;
  register: (email: string, password: string, displayName?: string) => Promise<{ success: boolean; error?: string; isFirstUser?: boolean }>;
  refresh: () => Promise<void>;
  refreshUser: () => Promise<void>;
}

const AuthContext = createContext<AuthContextType | undefined>(undefined);

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<ExtendedUser | null>(null);
  const [isLoading, setIsLoading] = useState(true);

  const refresh = async () => {
    try {
      const response = await authApi.getMe();
      if (response.isAuthenticated && response.user) {
        setUser(prev => {
          if (!prev) {
            console.info('[Auth] Session restored for', response.user!.email);
          }
          return response.user!;
        });
      } else {
        setUser(prev => {
          if (prev) {
            console.warn('[Auth] Session lost — /api/auth/me returned isAuthenticated=false', {
              previousUser: prev.email,
              response,
            });
          }
          return null;
        });
      }
    } catch (error) {
      setUser(prev => {
        if (prev) {
          console.error('[Auth] Session check failed — /api/auth/me threw', {
            previousUser: prev.email,
            error,
          });
        }
        return null;
      });
    }
  };

  // Handle 401 responses from the axios interceptor by clearing user state.
  // This lets ProtectedRoute redirect to login via React Router instead of
  // a hard window.location.href redirect that causes full page reloads.
  const handleAuthLost = useCallback(() => {
    setUser(prev => {
      if (prev) {
        console.warn('[Auth] Auth lost callback triggered (401 from API). Clearing user:', prev.email);
      }
      return null;
    });
  }, []);

  useEffect(() => {
    setOnAuthLost(handleAuthLost);
    return () => setOnAuthLost(null);
  }, [handleAuthLost]);

  useEffect(() => {
    const init = async () => {
      await refresh();
      setIsLoading(false);
    };
    init();
  }, []);

  // Default to a persistent cookie session unless explicitly disabled.
  const login = async (email: string, password: string, rememberMe = true) => {
    console.info('[Auth] Login attempt', { email, rememberMe });
    const response = await authApi.login({ email, password, rememberMe });
    if (response.success && response.user) {
      console.info('[Auth] Login succeeded for', response.user.email);
      setUser(response.user);
      return { success: true };
    }
    console.warn('[Auth] Login failed', { email, error: response.error });
    return { success: false, error: response.error || 'Login failed' };
  };

  const logout = async () => {
    console.info('[Auth] Logout requested');
    await authApi.logout();
    setUser(null);
  };

  const devLogin = async () => {
    const response = await authApi.devLogin();
    if (response.success && response.user) {
      setUser(response.user);
      return { success: true };
    }
    return { success: false, error: response.error || 'Development login failed' };
  };

  const register = async (email: string, password: string, displayName?: string) => {
    const response = await authApi.register({
      email,
      password,
      confirmPassword: password,
      displayName: displayName || null,
    });
    if (response.success && response.user) {
      setUser(response.user);
      return { success: true, isFirstUser: response.isFirstUser };
    }
    return { success: false, error: response.error || 'Registration failed' };
  };

  return (
    <AuthContext.Provider
      value={{
        user,
        isLoading,
        isAuthenticated: !!user,
        login,
        devLogin,
        logout,
        register,
        refresh,
        refreshUser: refresh,
      }}
    >
      {children}
    </AuthContext.Provider>
  );
}

export function useAuth() {
  const context = useContext(AuthContext);
  if (context === undefined) {
    throw new Error('useAuth must be used within an AuthProvider');
  }
  return context;
}
