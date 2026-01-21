// =============================================================================
// components/layout/Layout.tsx
//
// Main application layout with navigation.
// =============================================================================

import { Link, Outlet, useNavigate } from 'react-router-dom';
import { useAuth } from '@/context/AuthContext';
import { Button } from '@/components/ui/Button';

export function Layout() {
  const { user, logout, isAuthenticated } = useAuth();
  const navigate = useNavigate();

  const handleLogout = async () => {
    await logout();
    navigate('/auth/login');
  };

  return (
    <div className="min-h-screen bg-gray-50">
      {/* Navigation */}
      <nav className="bg-white shadow-sm border-b border-gray-200">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
          <div className="flex justify-between h-16">
            <div className="flex">
              {/* Logo */}
              <Link to="/" className="flex items-center">
                <span className="text-xl font-bold text-primary-600">Ando CI</span>
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
                    <span className="text-sm text-warning-600">
                      Email not verified
                    </span>
                  )}
                  <span className="text-sm text-gray-700">
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
      <main className="max-w-7xl mx-auto py-6 px-4 sm:px-6 lg:px-8">
        <Outlet />
      </main>
    </div>
  );
}

function NavLink({ to, children }: { to: string; children: React.ReactNode }) {
  return (
    <Link
      to={to}
      className="inline-flex items-center px-1 pt-1 text-sm font-medium text-gray-700 hover:text-primary-600 border-b-2 border-transparent hover:border-primary-300"
    >
      {children}
    </Link>
  );
}
