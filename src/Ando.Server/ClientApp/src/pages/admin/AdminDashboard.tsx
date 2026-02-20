// =============================================================================
// pages/admin/AdminDashboard.tsx
//
// Admin dashboard page with system overview.
// =============================================================================

import { useMutation, useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import {
  getAdminDashboard,
  getAdminProjects,
  getSystemHealth,
  getSystemUpdateStatus,
  triggerSystemUpdate,
} from '@/api/admin';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge, getBuildStatusVariant } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
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
  const {
    data: systemHealth,
    isLoading: systemHealthLoading,
    error: systemHealthError,
  } = useQuery({
    queryKey: ['admin-system-health'],
    queryFn: getSystemHealth,
    refetchInterval: 1000 * 60,
    retry: false,
  });
  const {
    data: updateStatus,
    isLoading: updateStatusLoading,
    refetch: refetchUpdateStatus,
  } = useQuery({
    queryKey: ['admin-system-update-status'],
    queryFn: () => getSystemUpdateStatus(false),
    refetchInterval: 1000 * 60 * 5,
    retry: false,
  });
  const triggerUpdateMutation = useMutation({
    mutationFn: triggerSystemUpdate,
    onSuccess: () => {
      void refetchUpdateStatus();
    },
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
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">Admin Dashboard</h1>
          <p className="text-gray-500 dark:text-slate-400">System overview and management</p>
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
          className={dashboard?.activeBuilds ? 'text-success-600 dark:text-success-400' : ''}
        />
      </div>

      {/* Quick Links */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        <Link
          to="/admin/users"
          className="bg-white border border-gray-200 rounded-xl p-6 hover:shadow-md transition-shadow dark:bg-slate-900 dark:hover:shadow-slate-800/50"
        >
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-100">User Management</h2>
          <p className="text-gray-500 mt-1 dark:text-slate-400">
            Manage users, roles, and account status
          </p>
          <div className="mt-4 text-primary-600 font-medium dark:text-primary-400">
            View all users &rarr;
          </div>
        </Link>

        <div className="bg-white border border-gray-200 rounded-xl p-6 dark:bg-slate-900 dark:border-slate-800">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-100">System Health</h2>
          {systemHealthLoading ? (
            <p className="mt-4 text-sm text-gray-500 dark:text-slate-400">Probing health...</p>
          ) : systemHealthError ? (
            <Alert variant="error" className="mt-4 text-sm">
              Failed to probe system health
            </Alert>
          ) : (
            <div className="mt-4 space-y-2">
              {(systemHealth?.checks ?? []).map((check) => (
                <HealthItem
                  key={check.name}
                  label={check.name}
                  status={check.status}
                  message={check.message}
                />
              ))}
            </div>
          )}
        </div>

        <div className="bg-white border border-gray-200 rounded-xl p-6 dark:bg-slate-900 dark:border-slate-800">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-100">Server Updates</h2>

          {updateStatusLoading ? (
            <p className="mt-4 text-sm text-gray-500 dark:text-slate-400">Checking update status...</p>
          ) : !updateStatus?.enabled ? (
            <p className="mt-4 text-sm text-gray-500 dark:text-slate-400">
              Self-update is disabled. Set <code>SelfUpdate__Enabled=true</code> in server config to enable.
            </p>
          ) : (
            <div className="mt-4 space-y-3">
              <p className="text-sm text-gray-700 dark:text-slate-300">
                {updateStatus.isUpdateInProgress
                  ? 'Update in progress. The service may restart shortly.'
                  : updateStatus.isUpdateAvailable
                    ? 'A newer server image is available.'
                    : 'Server is up to date.'}
              </p>

              {(updateStatus.currentVersion || updateStatus.latestVersion) && (
                <p className="text-xs text-gray-500 dark:text-slate-400 font-mono">
                  {updateStatus.currentVersion ?? 'current'} -&gt; {updateStatus.latestVersion ?? 'latest'}
                </p>
              )}

              {updateStatus.lastError && (
                <Alert variant="error" className="text-sm">
                  Update check error: {updateStatus.lastError}
                </Alert>
              )}

              <div className="flex flex-wrap items-center gap-2">
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => void refetchUpdateStatus()}
                  disabled={updateStatus.isChecking || triggerUpdateMutation.isPending}
                >
                  Refresh
                </Button>

                <Button
                  variant="primary"
                  size="sm"
                  onClick={() => triggerUpdateMutation.mutate()}
                  isLoading={triggerUpdateMutation.isPending}
                  disabled={
                    !updateStatus.isUpdateAvailable ||
                    updateStatus.isUpdateInProgress ||
                    updateStatus.isChecking ||
                    triggerUpdateMutation.isPending
                  }
                >
                  Update now
                </Button>
              </div>

              {triggerUpdateMutation.data && (
                <p className="text-xs text-gray-500 dark:text-slate-400">
                  {triggerUpdateMutation.data.message}
                </p>
              )}
            </div>
          )}
        </div>
      </div>

      {/* All Projects */}
      <div className="bg-white border border-gray-200 rounded-xl dark:bg-slate-900 dark:border-slate-800">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-slate-800">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-100">All Projects</h2>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full divide-y divide-gray-100 dark:divide-slate-800">
            <thead className="bg-gray-50 dark:bg-slate-800">
              <tr>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  Project
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  Owner
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  Builds
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  Status
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  Last Build
                </th>
              </tr>
            </thead>
            <tbody className="bg-white divide-y divide-gray-200 dark:bg-slate-900 dark:divide-slate-700">
              {projects.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-6 py-8 text-center text-gray-500 dark:text-slate-400">
                    No projects yet
                  </td>
                </tr>
              ) : (
                projects.map((project) => (
                  <tr key={project.id} className="hover:bg-gray-50 dark:hover:bg-slate-800">
                    <td className="px-6 py-4 whitespace-nowrap">
                      <Link
                        to={`/projects/${project.id}`}
                        className="text-primary-600 hover:underline font-medium dark:text-primary-400"
                      >
                        {project.repoFullName}
                      </Link>
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-slate-400">
                      {project.ownerEmail}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-slate-400">
                      {project.totalBuilds}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap">
                      {project.lastBuildStatus ? (
                        <Badge variant={getBuildStatusVariant(project.lastBuildStatus)}>
                          {project.lastBuildStatus}
                        </Badge>
                      ) : (
                        <span className="text-gray-400 dark:text-slate-1000">-</span>
                      )}
                    </td>
                    <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500 dark:text-slate-400">
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
    <div className="bg-white border border-gray-200 rounded-xl px-4 py-5 sm:p-6 dark:bg-slate-900 dark:border-slate-800">
      <dt className="text-sm font-medium text-gray-500 truncate dark:text-slate-400">{title}</dt>
      <dd className={`mt-1 text-3xl font-semibold text-gray-900 dark:text-slate-100 ${className}`}>{value}</dd>
    </div>
  );
}

function HealthItem({
  label,
  status,
  message,
}: {
  label: string;
  status: 'healthy' | 'warning' | 'error';
  message?: string;
}) {
  const colors = {
    healthy: 'bg-success-500',
    warning: 'bg-warning-500',
    error: 'bg-error-500',
  };

  return (
    <div className="flex items-center justify-between text-sm">
      <span className="text-gray-600 dark:text-slate-400">{label}</span>
      <div className="flex items-center">
        <span className={`w-2 h-2 rounded-full ${colors[status]} mr-2`}></span>
        <span className="text-gray-900 capitalize dark:text-slate-100">{status}</span>
      </div>
      {message && (
        <span className="text-xs text-gray-400 dark:text-slate-500 ml-2 truncate max-w-[160px]" title={message}>
          {message}
        </span>
      )}
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
