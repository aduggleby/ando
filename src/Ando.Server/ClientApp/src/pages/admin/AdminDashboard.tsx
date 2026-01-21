// =============================================================================
// pages/admin/AdminDashboard.tsx
//
// Admin dashboard page with system overview.
// =============================================================================

import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { getAdminDashboard, getAdminProjects } from '@/api/admin';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge, getBuildStatusVariant } from '@/components/ui/Badge';
import { useAuth } from '@/context/AuthContext';

export function AdminDashboard() {
  const { user } = useAuth();

  const { data: dashboardData, isLoading: dashboardLoading, error: dashboardError } = useQuery({
    queryKey: ['admin-dashboard'],
    queryFn: getAdminDashboard,
  });

  const { data: projectsData, isLoading: projectsLoading } = useQuery({
    queryKey: ['admin-projects'],
    queryFn: getAdminProjects,
  });

  if (dashboardLoading || projectsLoading) {
    return <Loading size="lg" className="py-12" text="Loading admin dashboard..." />;
  }

  if (dashboardError) {
    return <Alert variant="error">Failed to load admin dashboard</Alert>;
  }

  const dashboard = dashboardData?.dashboard;
  const projects = projectsData?.projects || [];

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">Admin Dashboard</h1>
          <p className="text-gray-500">System overview and management</p>
        </div>
        {user?.impersonating && (
          <Alert variant="warning" className="text-sm">
            You are impersonating another user
          </Alert>
        )}
      </div>

      {/* System Stats */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <StatCard title="Total Users" value={dashboard?.totalUsers || 0} />
        <StatCard title="Total Projects" value={dashboard?.totalProjects || 0} />
        <StatCard title="Total Builds" value={dashboard?.totalBuilds || 0} />
        <StatCard
          title="Active Builds"
          value={dashboard?.activeBuilds || 0}
          className={dashboard?.activeBuilds ? 'text-success-600' : ''}
        />
      </div>

      {/* Quick Links */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <Link
          to="/admin/users"
          className="bg-white shadow rounded-lg p-6 hover:shadow-md transition-shadow"
        >
          <h2 className="text-lg font-medium text-gray-900">User Management</h2>
          <p className="text-gray-500 mt-1">
            Manage users, roles, and account status
          </p>
          <div className="mt-4 text-primary-600 font-medium">
            View all users &rarr;
          </div>
        </Link>

        <div className="bg-white shadow rounded-lg p-6">
          <h2 className="text-lg font-medium text-gray-900">System Health</h2>
          <div className="mt-4 space-y-2">
            <HealthItem label="Database" status="healthy" />
            <HealthItem label="Background Jobs" status="healthy" />
            <HealthItem label="GitHub Integration" status="healthy" />
          </div>
        </div>
      </div>

      {/* All Projects */}
      <div className="bg-white shadow rounded-lg">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200">
          <h2 className="text-lg font-medium text-gray-900">All Projects</h2>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-200">
            <thead className="bg-gray-50">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Project
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Owner
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Builds
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Status
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                  Last Build
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200">
              {projects.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-6 py-8 text-center text-gray-500">
                    No projects yet
                  </td>
                </tr>
              ) : (
                projects.map((project) => (
                  <tr key={project.id} className="hover:bg-gray-50">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <Link
                        to={`/projects/${project.id}`}
                        className="text-primary-600 hover:underline font-medium"
                      >
                        {project.repoFullName}
                      </Link>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {project.ownerEmail}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {project.totalBuilds}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {project.lastBuildStatus ? (
                        <Badge variant={getBuildStatusVariant(project.lastBuildStatus)}>
                          {project.lastBuildStatus}
                        </Badge>
                      ) : (
                        <span className="text-gray-400">-</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                      {project.lastBuildAt
                        ? formatRelativeTime(project.lastBuildAt)
                        : 'Never'}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
      </div>
    </div>
  );
}

function StatCard({ title, value, className = '' }: { title: string; value: number; className?: string }) {
  return (
    <div className="bg-white shadow rounded-lg px-4 py-5 sm:p-6">
      <dt className="text-sm font-medium text-gray-500 truncate">{title}</dt>
      <dd className={`mt-1 text-3xl font-semibold text-gray-900 ${className}`}>{value}</dd>
    </div>
  );
}

function HealthItem({ label, status }: { label: string; status: 'healthy' | 'warning' | 'error' }) {
  const colors = {
    healthy: 'bg-success-500',
    warning: 'bg-warning-500',
    error: 'bg-error-500',
  };

  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-gray-600">{label}</span>
      <div className="flex items-center">
        <span className={`w-2 h-2 rounded-full ${colors[status]} mr-2`}></span>
        <span className="text-gray-900 capitalize">{status}</span>
      </div>
    </div>
  );
}

function formatRelativeTime(dateStr: string): string {
  const date = new Date(dateStr);
  const now = new Date();
  const diffMs = now.getTime() - date.getTime();
  const diffMins = Math.floor(diffMs / 60000);

  if (diffMins < 1) return 'just now';
  if (diffMins < 60) return `${diffMins}m ago`;

  const diffHours = Math.floor(diffMins / 60);
  if (diffHours < 24) return `${diffHours}h ago`;

  const diffDays = Math.floor(diffHours / 24);
  if (diffDays < 7) return `${diffDays}d ago`;

  return date.toLocaleDateString();
}
