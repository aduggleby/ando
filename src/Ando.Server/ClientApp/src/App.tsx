// =============================================================================
// App.tsx
//
// Main application component with routing configuration.
// =============================================================================

import { BrowserRouter, Routes, Route, Navigate, useLocation } from 'react-router-dom';
import { QueryProvider } from '@/context/QueryProvider';
import { AuthProvider, useAuth } from '@/context/AuthContext';
import { Layout } from '@/components/layout/Layout';
import { Loading } from '@/components/ui/Loading';

// Pages
import { Home } from '@/pages/Home';
import { Login } from '@/pages/auth/Login';
import { Register } from '@/pages/auth/Register';
import { ForgotPassword } from '@/pages/auth/ForgotPassword';
import { ResetPassword } from '@/pages/auth/ResetPassword';
import { VerifyEmail } from '@/pages/auth/VerifyEmail';
import { RegisterSuccess } from '@/pages/auth/RegisterSuccess';
import { AccessDenied } from '@/pages/auth/AccessDenied';
import { ProjectList } from '@/pages/projects/ProjectList';
import { ProjectDetails } from '@/pages/projects/ProjectDetails';
import { ProjectCreate } from '@/pages/projects/ProjectCreate';
import { ProjectSettings } from '@/pages/projects/ProjectSettings';
import { ProjectStatus } from '@/pages/projects/ProjectStatus';
import { BuildDetails } from '@/pages/builds/BuildDetails';
import { AdminDashboard } from '@/pages/admin/AdminDashboard';
import { UserManagement } from '@/pages/admin/UserManagement';
import { NotFound } from '@/pages/NotFound';

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading..." />;
  }

  if (!isAuthenticated) {
    // Preserve the current URL so login can redirect back after success.
    const returnUrl = location.pathname + location.search;
    const loginPath = returnUrl && returnUrl !== '/'
      ? `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`
      : '/auth/login';
    return <Navigate to={loginPath} replace />;
  }

  return <>{children}</>;
}

function AdminRoute({ children }: { children: React.ReactNode }) {
  const { user, isAuthenticated, isLoading } = useAuth();
  const location = useLocation();

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading..." />;
  }

  if (!isAuthenticated) {
    const returnUrl = location.pathname + location.search;
    const loginPath = returnUrl && returnUrl !== '/'
      ? `/auth/login?returnUrl=${encodeURIComponent(returnUrl)}`
      : '/auth/login';
    return <Navigate to={loginPath} replace />;
  }

  if (!user?.isAdmin) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}

function PublicOnlyRoute({ children }: { children: React.ReactNode }) {
  const { isAuthenticated, isLoading } = useAuth();

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading..." />;
  }

  if (isAuthenticated) {
    return <Navigate to="/" replace />;
  }

  return <>{children}</>;
}

function AppRoutes() {
  return (
    <Routes>
      {/* Public auth routes */}
      <Route
        path="/auth/login"
        element={
          <PublicOnlyRoute>
            <Login />
          </PublicOnlyRoute>
        }
      />
      <Route
        path="/auth/register"
        element={
          <PublicOnlyRoute>
            <Register />
          </PublicOnlyRoute>
        }
      />
      <Route path="/auth/forgot-password" element={<ForgotPassword />} />
      <Route path="/auth/reset-password" element={<ResetPassword />} />
      <Route path="/auth/verify-email" element={<VerifyEmail />} />
      <Route path="/auth/register-success" element={<RegisterSuccess />} />
      <Route path="/auth/access-denied" element={<AccessDenied />} />

      {/* Protected routes with layout */}
      <Route
        element={
          <ProtectedRoute>
            <Layout />
          </ProtectedRoute>
        }
      >
        <Route index element={<Home />} />
        <Route path="projects" element={<ProjectList />} />
        <Route path="projects/create" element={<ProjectCreate />} />
        <Route path="projects/status" element={<ProjectStatus />} />
        <Route path="projects/:id" element={<ProjectDetails />} />
        <Route path="projects/:id/settings" element={<ProjectSettings />} />
        <Route path="builds/:id" element={<BuildDetails />} />

        {/* Admin routes */}
        <Route
          path="admin"
          element={
            <AdminRoute>
              <AdminDashboard />
            </AdminRoute>
          }
        />
        <Route
          path="admin/users"
          element={
            <AdminRoute>
              <UserManagement />
            </AdminRoute>
          }
        />
      </Route>

      {/* 404 */}
      <Route path="*" element={<NotFound />} />
    </Routes>
  );
}

export function App() {
  return (
    <BrowserRouter>
      <QueryProvider>
        <AuthProvider>
          <AppRoutes />
        </AuthProvider>
      </QueryProvider>
    </BrowserRouter>
  );
}
