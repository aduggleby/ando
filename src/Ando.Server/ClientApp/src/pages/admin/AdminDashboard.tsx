// =============================================================================
// pages/admin/AdminDashboard.tsx
//
// Admin dashboard page with system overview.
// =============================================================================

import { useMutation, useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { useMemo, useState } from 'react';
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
import { ServerUpdateOverlay } from '@/components/layout/ServerUpdateOverlay';
import { useServerUpdateFlow } from '@/components/layout/useServerUpdateFlow';
import { useSystemUpdateRefresh } from '@/hooks/useSystemUpdateRefresh';

export function AdminDashboard() {
  const { user } = useAuth();
  const updateFlow = useServerUpdateFlow();
  useSystemUpdateRefresh(true);
  const [projectSort, setProjectSort] = useState<{
    key: 'project' | 'owner' | 'builds' | 'status' | 'lastBuild';
    direction: 'asc' | 'desc';
  }>({ key: 'lastBuild', direction: 'desc' });

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
      updateFlow.start();
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
  const sortedProjects = useMemo(() => {
    const sorted = [...projects];
    sorted.sort((a, b) => {
      const direction = projectSort.direction === 'asc' ? 1 : -1;

      switch (projectSort.key) {
        case 'project':
          return a.repoFullName.localeCompare(b.repoFullName) * direction;
        case 'owner':
          return a.ownerEmail.localeCompare(b.ownerEmail) * direction;
        case 'builds':
          return (a.totalBuilds - b.totalBuilds) * direction;
        case 'status':
          return ((a.lastBuildStatus ?? '').localeCompare(b.lastBuildStatus ?? '')) * direction;
        case 'lastBuild': {
          const aTime = a.lastBuildAt ? new Date(a.lastBuildAt).getTime() : -1;
          const bTime = b.lastBuildAt ? new Date(b.lastBuildAt).getTime() : -1;
          return (aTime - bTime) * direction;
        }
      }
    });
    return sorted;
  }, [projects, projectSort]);

  const toggleProjectSort = (key: 'project' | 'owner' | 'builds' | 'status' | 'lastBuild') => {
    setProjectSort((current) => {
      if (current.key === key) {
        return { key, direction: current.direction === 'asc' ? 'desc' : 'asc' };
      }
      return { key, direction: key === 'lastBuild' ? 'desc' : 'asc' };
    });
  };

  return (
    <div className="space-y-6">
      {updateFlow.isVisible && (
        <ServerUpdateOverlay
          phase={updateFlow.phase === 'reconnecting' ? 'reconnecting' : 'countdown'}
          remainingSeconds={updateFlow.remainingSeconds}
          attempts={updateFlow.attempts}
        />
      )}

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
            <div className="mt-4 overflow-x-auto">
              <table className="min-w-full text-sm">
                <thead>
                  <tr className="border-b border-gray-200 dark:border-slate-800">
                    <th className="py-2 pr-3 text-left font-medium text-gray-500 dark:text-slate-400">Check</th>
                    <th className="py-2 pr-3 text-left font-medium text-gray-500 dark:text-slate-400">Status</th>
                    <th className="py-2 text-left font-medium text-gray-500 dark:text-slate-400">Message</th>
                  </tr>
                </thead>
                <tbody>
                  {(systemHealth?.checks ?? []).map((check) => (
                    <HealthItemRow
                      key={check.name}
                      label={check.name}
                      status={check.status}
                      message={check.message}
                    />
                  ))}
                </tbody>
              </table>
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
                  <SortButton
                    label="Project"
                    active={projectSort.key === 'project'}
                    direction={projectSort.direction}
                    onClick={() => toggleProjectSort('project')}
                  />
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  <SortButton
                    label="Owner"
                    active={projectSort.key === 'owner'}
                    direction={projectSort.direction}
                    onClick={() => toggleProjectSort('owner')}
                  />
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  <SortButton
                    label="Builds"
                    active={projectSort.key === 'builds'}
                    direction={projectSort.direction}
                    onClick={() => toggleProjectSort('builds')}
                  />
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  <SortButton
                    label="Status"
                    active={projectSort.key === 'status'}
                    direction={projectSort.direction}
                    onClick={() => toggleProjectSort('status')}
                  />
                </th>
                <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase dark:text-slate-400">
                  <SortButton
                    label="Last Build"
                    active={projectSort.key === 'lastBuild'}
                    direction={projectSort.direction}
                    onClick={() => toggleProjectSort('lastBuild')}
                  />
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
                sortedProjects.map((project) => (
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

function SortButton({
  label,
  active,
  direction,
  onClick,
}: {
  label: string;
  active: boolean;
  direction: 'asc' | 'desc';
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      className="inline-flex items-center gap-1 hover:text-gray-700 dark:hover:text-slate-200"
    >
      <span>{label}</span>
      {active && <span>{direction === 'asc' ? '^' : 'v'}</span>}
    </button>
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

function HealthItemRow({
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
    <tr className="border-b border-gray-100 last:border-0 dark:border-slate-800">
      <td className="py-2 pr-3 text-gray-600 dark:text-slate-400 whitespace-nowrap">{label}</td>
      <td className="py-2 pr-3 whitespace-nowrap">
        <div className="flex items-center">
          <span className={`w-2 h-2 rounded-full ${colors[status]} mr-2`}></span>
          <span className="text-gray-900 capitalize dark:text-slate-100">{status}</span>
        </div>
      </td>
      <td className="py-2 text-xs text-gray-400 dark:text-slate-500">
        {message ? <span title={message}>{message}</span> : '-'}
      </td>
    </tr>
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
