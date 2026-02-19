// =============================================================================
// components/layout/Layout.tsx
//
// Main application layout with navigation.
// =============================================================================

import { Link, Outlet, useNavigate } from 'react-router-dom';
import { useQuery } from '@tanstack/react-query';
import { useAuth } from '@/context/AuthContext';
import { Button } from '@/components/ui/Button';
import { getAppVersion } from '@/api/system';

export function Layout() {
  const { user, logout, isAuthenticated } = useAuth();
  const navigate = useNavigate();
  const { data: appVersion } = useQuery({
    queryKey: ['app-version'],
    queryFn: getAppVersion,
    staleTime: 1000 * 60 * 5,
  });

  const handleLogout = async () => {
    await logout();
    navigate('/auth/login');
  };

  return (
    <div className="min-h-screen bg-gray-50 dark:bg-slate-950 flex flex-col">
      {/* Navigation */}
      <nav className="bg-white shadow-sm border-b border-gray-200 dark:bg-slate-900 dark:border-slate-700">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between h-16">
            <div className="flex">
              {/* Logo */}
              <Link to="/" className="flex items-center">
                <span className="text-xl font-bold text-primary-600 dark:text-primary-400">Ando CI</span>
              </Link>

              {/* Navigation Links */}
              {isAuthenticated && (
                <div className="hidden sm:ml-8 sm:flex sm:space-x-8">
                  <NavLink to="/">Dashboard</NavLink>
                  <NavLink to="/projects">Projects</NavLink>
                  <NavLink to="/projects/status">Status</NavLink>
                  {user?.isAdmin && <NavLink to="/admin">Admin</NavLink>}
                </div>
              )}
            </div>

            {/* Right side */}
            <div className="flex items-center">
              {isAuthenticated ? (
                <div className="flex items-center space-x-4">
                  {!user?.emailVerified && (
                    <span className="text-sm text-warning-600 dark:text-warning-400">
                      Email not verified
                    </span>
                  )}
                  <span className="text-sm text-gray-700 dark:text-slate-300">
                    {user?.displayName || user?.email}
                  </span>
                  <Button variant="ghost" size="sm" onClick={handleLogout}>
                    Logout
                  </Button>
                </div>
              ) : (
                <div className="flex items-center space-x-4">
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

      <footer className="border-t border-gray-200 bg-white dark:border-slate-700 dark:bg-slate-900">
        <div className="max-w-7xl mx-auto px-4 py-3 sm:px-6 lg:px-8 text-xs text-gray-500 dark:text-slate-400">
          Version <code className="font-mono">{appVersion?.version || '-'}</code>
        </div>
      </footer>
    </div>
  );
}

function NavLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <Link
      to={to}
      className="inline-flex items-center px-1 pt-1 text-sm font-medium text-gray-700 hover:text-primary-600 border-b-2 border-transparent hover:border-primary-300 dark:text-slate-300 dark:hover:text-primary-400 dark:hover:border-primary-500"
    >
      {children}
    </Link>
  );
}
