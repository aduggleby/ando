// =============================================================================
// components/layout/Layout.tsx
//
// Phosphor design — frosted glass nav, theme toggle, gradient logo.
// =============================================================================

import { Link, Outlet, useNavigate, useLocation } from 'react-router-dom';
import { useMutation, useQuery } from '@tanstack/react-query';
import { useAuth } from '@/context/AuthContext';
import { useTheme } from '@/context/ThemeContext';
import { Button } from '@/components/ui/Button';
import { getAppVersion } from '@/api/system';
import { getSystemUpdateStatus, triggerSystemUpdate } from '@/api/admin';
import { ServerUpdateOverlay } from './ServerUpdateOverlay';
import { useServerUpdateFlow } from './useServerUpdateFlow';
import { useSystemUpdateRefresh } from '@/hooks/useSystemUpdateRefresh';

export function Layout() {
  const { user, logout, isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const { preference, setPreference, resolved } = useTheme();
  const updateFlow = useServerUpdateFlow();
  const { data: appVersion } = useQuery({
    queryKey: ['app-version'],
    queryFn: getAppVersion,
    staleTime: 1000 * 60 * 5,
  });
  const { data: systemUpdateStatus } = useQuery({
    queryKey: ['system-update-status'],
    queryFn: () => getSystemUpdateStatus(false),
    enabled: isAuthenticated && !!user?.isAdmin,
    refetchInterval: 1000 * 60 * 5,
    staleTime: 1000 * 30,
    retry: false,
  });
  const updateMutation = useMutation({
    mutationFn: triggerSystemUpdate,
    onSuccess: () => {
      updateFlow.start();
    },
  });
  useSystemUpdateRefresh(isAuthenticated && !!user?.isAdmin);

  const handleLogout = async () => {
    await logout();
    navigate('/auth/login');
  };

  const navLinks = [
    { to: '/', label: 'Dashboard', exact: true },
    { to: '/projects', label: 'Projects' },
    { to: '/settings/api-tokens', label: 'API Tokens' },
    ...(user?.isAdmin ? [{ to: '/admin', label: 'Admin' }] : []),
  ];

  function isActive(to: string, exact?: boolean) {
    if (exact) return location.pathname === to;
    return location.pathname.startsWith(to);
  }

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-slate-950 flex flex-col">
      {updateFlow.isVisible && (
        <ServerUpdateOverlay
          phase={updateFlow.phase === 'reconnecting' ? 'reconnecting' : 'countdown'}
          remainingSeconds={updateFlow.remainingSeconds}
          attempts={updateFlow.attempts}
        />
      )}

      {isAuthenticated && user?.isAdmin && systemUpdateStatus?.enabled && (
        <SelfUpdateBanner
          status={systemUpdateStatus}
          isUpdating={updateMutation.isPending}
          onUpdate={() => updateMutation.mutate()}
        />
      )}

      {/* Navigation */}
      <nav className="phosphor-glass sticky top-0 z-50 border-b border-gray-200 dark:border-slate-800">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between h-14">
            <div className="flex items-center gap-8">
              {/* Logo */}
              <Link to="/" className="flex items-center gap-2.5">
                <div className="w-7 h-7 rounded-lg bg-gradient-to-br from-primary-400 to-purple-500 flex items-center justify-center text-white text-xs font-bold shadow-lg shadow-primary-500/20 dark:shadow-primary-500/30">
                  A
                </div>
                <span className="text-lg font-bold text-gray-900 dark:text-slate-100 tracking-tight">Ando</span>
              </Link>

              {/* Navigation Links */}
              {isAuthenticated && (
                <div className="hidden sm:flex items-center gap-1">
                  {navLinks.map((link) => (
                    <Link
                      key={link.to}
                      to={link.to}
                      className={`
                        px-3 py-1.5 rounded-lg text-[13px] font-medium transition-all
                        ${isActive(link.to, link.exact)
                          ? 'text-primary-700 bg-primary-50 dark:text-primary-400 dark:bg-primary-500/10'
                          : 'text-gray-500 hover:text-gray-900 hover:bg-gray-100 dark:text-slate-400 dark:hover:text-slate-200 dark:hover:bg-slate-800'
                        }
                      `}
                    >
                      {link.label}
                    </Link>
                  ))}
                </div>
              )}
            </div>

            {/* Right side */}
            <div className="flex items-center gap-3">
              {/* Theme switcher */}
              <ThemeSwitcher preference={preference} setPreference={setPreference} resolved={resolved} />

              {isAuthenticated ? (
                <div className="flex items-center gap-3">
                  {!user?.emailVerified && (
                    <span className="text-xs text-warning-600 dark:text-warning-400">
                      Email not verified
                    </span>
                  )}
                  <span className="text-xs text-gray-500 dark:text-slate-400 bg-gray-100 dark:bg-slate-800 border border-gray-200 dark:border-slate-700 px-2.5 py-1 rounded-full">
                    {user?.displayName || user?.email}
                  </span>
                  <Button variant="ghost" size="sm" onClick={handleLogout}>
                    Sign out
                  </Button>
                </div>
              ) : (
                <div className="flex items-center gap-3">
                  <Link to="/auth/login">
                    <Button variant="ghost" size="sm">Login</Button>
                  </Link>
                  <Link to="/auth/register">
                    <Button variant="primary" size="sm">Register</Button>
                  </Link>
                </div>
              )}
            </div>
          </div>
        </div>
      </nav>

      {/* Main content */}
      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8 w-full flex-1">
        <Outlet />
      </main>

      <footer className="border-t border-gray-200 dark:border-slate-800">
        <div className="max-w-7xl mx-auto px-4 py-3 sm:px-6 lg:px-8 text-xs text-gray-400 dark:text-slate-500 font-mono">
          Ando <span className="text-gray-300 dark:text-slate-600 mx-1">·</span> {appVersion?.version || '-'}
        </div>
      </footer>
    </div>
  );
}

function SelfUpdateBanner({
  status,
  isUpdating,
  onUpdate,
}: {
  status: {
    isChecking: boolean;
    isUpdateAvailable: boolean;
    isUpdateInProgress: boolean;
    currentVersion: string | null;
    latestVersion: string | null;
    lastError: string | null;
  };
  isUpdating: boolean;
  onUpdate: () => void;
}) {
  if (!status.isUpdateAvailable && !status.isUpdateInProgress && !status.lastError) {
    return null;
  }

  return (
    <div className="border-b border-warning-200 bg-warning-50 dark:border-warning-500/40 dark:bg-warning-500/15">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 py-2.5 flex flex-wrap items-center gap-3 text-sm">
        {status.isUpdateInProgress ? (
          <span className="text-warning-800 dark:text-warning-100">
            Server update in progress. The app may restart shortly.
          </span>
        ) : status.isUpdateAvailable ? (
          <span className="text-warning-800 dark:text-warning-100">
            New server image available
            {status.currentVersion || status.latestVersion
              ? ` (${status.currentVersion ?? 'current'} -> ${status.latestVersion ?? 'latest'})`
              : ''}
            .
          </span>
        ) : null}

        {status.lastError && (
          <span className="text-error-700 dark:text-error-300">
            Update check error: {status.lastError}
          </span>
        )}

        {status.isUpdateAvailable && !status.isUpdateInProgress && (
          <Button
            variant="primary"
            size="sm"
            onClick={onUpdate}
            isLoading={isUpdating}
            disabled={status.isChecking || isUpdating}
          >
            Update now
          </Button>
        )}
      </div>
    </div>
  );
}

function ThemeSwitcher({
  preference,
  setPreference,
  resolved,
}: {
  preference: string;
  setPreference: (p: 'system' | 'light' | 'dark') => void;
  resolved: string;
}) {
  const cycle = () => {
    // Cycle: system → light → dark → system
    if (preference === 'system') setPreference('light');
    else if (preference === 'light') setPreference('dark');
    else setPreference('system');
  };

  return (
    <button
      onClick={cycle}
      className="flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-medium text-gray-500 hover:text-gray-900 hover:bg-gray-100 dark:text-slate-400 dark:hover:text-slate-200 dark:hover:bg-slate-800 transition-all"
      title={`Theme: ${preference} (${resolved})`}
    >
      {resolved === 'dark' ? (
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M20.354 15.354A9 9 0 018.646 3.646 9.003 9.003 0 0012 21a9.003 9.003 0 008.354-5.646z" />
        </svg>
      ) : (
        <svg className="w-3.5 h-3.5" fill="none" viewBox="0 0 24 24" stroke="currentColor" strokeWidth={2}>
          <path strokeLinecap="round" strokeLinejoin="round" d="M12 3v1m0 16v1m9-9h-1M4 12H3m15.364 6.364l-.707-.707M6.343 6.343l-.707-.707m12.728 0l-.707.707M6.343 17.657l-.707.707M16 12a4 4 0 11-8 0 4 4 0 018 0z" />
        </svg>
      )}
      <span className="hidden sm:inline">
        {preference === 'system' ? 'Auto' : preference === 'light' ? 'Light' : 'Dark'}
      </span>
    </button>
  );
}
