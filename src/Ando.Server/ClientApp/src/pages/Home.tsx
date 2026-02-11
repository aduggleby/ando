// =============================================================================
// pages/Home.tsx
//
// Dashboard page showing recent builds and project statistics.
// =============================================================================

import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { getDashboard } from '@/api/dashboard';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge, getBuildStatusVariant } from '@/components/ui/Badge';

export function Home() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['dashboard'],
    queryFn: getDashboard,
  });

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading dashboard..." />;
  }

  if (error) {
    return <Alert variant="error">Failed to load dashboard</Alert>;
  }

  const dashboard = data?.dashboard;

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-50">Dashboard</h1>

      {/* Statistics */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-6">
        <StatCard title="Total Projects" value={dashboard?.totalProjects || 0} />
        <StatCard title="Builds Today" value={dashboard?.buildsToday || 0} />
        <StatCard
          title="Failed Today"
          value={dashboard?.failedToday || 0}
          className={dashboard?.failedToday ? 'text-error-600 dark:text-error-400' : ''}
        />
      </div>

      {/* Recent Builds */}
      <div className="bg-white shadow rounded-lg dark:bg-slate-900 dark:shadow-slate-900/50">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-slate-700">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-50">Recent Builds</h2>
        </div>
        <div className="divide-y divide-gray-200 dark:divide-slate-700">
          {dashboard?.recentBuilds.length === 0 ? (
            <div className="px-4 py-8 text-center text-gray-500 dark:text-slate-400">
              No builds yet.{' '}
              <Link to="/projects/create" className="text-primary-600 hover:text-primary-500 dark:text-primary-400">
                Create a project
              </Link>{' '}
              to get started.
            </div>
          ) : (
            dashboard?.recentBuilds.map((build) => (
              <Link
                key={build.id}
                to={`/builds/${build.id}`}
                className="block px-4 py-4 hover:bg-gray-50 dark:hover:bg-slate-800"
              >
                <div className="flex items-center justify-between">
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-gray-900 truncate dark:text-slate-100">
                      {build.projectName}
                    </p>
                    <p className="text-sm text-gray-500 dark:text-slate-400">
                      {build.branch} · {build.shortCommitSha}
                    </p>
                  </div>
                  <div className="flex items-center space-x-4">
                    <Badge variant={getBuildStatusVariant(build.status)}>
                      {build.status}
                    </Badge>
                    {build.duration && (
                      <span className="text-sm text-gray-500 dark:text-slate-400">{formatDuration(build.duration)}</span>
                    )}
                  </div>
                </div>
              </Link>
            ))
          )}
        </div>
      </div>
    </div>
  );
}

function StatCard({ title, value, className = '' }: { title: string; value: number; className?: string }) {
  return (
    <div className="bg-white shadow rounded-lg px-4 py-5 sm:p-6 dark:bg-slate-900 dark:shadow-slate-900/50">
      <dt className="text-sm font-medium text-gray-500 truncate dark:text-slate-400">{title}</dt>
      <dd className={`mt-1 text-3xl font-semibold text-gray-900 dark:text-slate-50 ${className}`}>{value}</dd>
    </div>
  );
}

function formatDuration(duration: string): string {
  // Duration comes as TimeSpan string like "00:01:23"
  const parts = duration.split(':');
  if (parts.length === 3) {
    const hours = parseInt(parts[0]);
    const minutes = parseInt(parts[1]);
    const seconds = parseInt(parts[2].split('.')[0]);

    if (hours > 0) return `${hours}h ${minutes}m`;
    if (minutes > 0) return `${minutes}m ${seconds}s`;
    return `${seconds}s`;
  }
  return duration;
}
