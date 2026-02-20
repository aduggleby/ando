// =============================================================================
// pages/projects/ProjectStatus.tsx
//
// Project status overview page showing all projects and their build status.
// =============================================================================

import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { getProjects } from '@/api/projects';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge, getBuildStatusVariant } from '@/components/ui/Badge';

export function ProjectStatus() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['projects'],
    queryFn: getProjects,
    refetchInterval: 30000, // Refresh every 30 seconds
  });

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading project status..." />;
  }

  if (error) {
    return <Alert variant="error">Failed to load project status</Alert>;
  }

  const projects = data?.projects || [];

  // Group projects by status
  const failed = projects.filter(p => p.lastBuildStatus === 'Failed');
  const building = projects.filter(p => p.lastBuildStatus === 'Running' || p.lastBuildStatus === 'Pending');
  const succeeded = projects.filter(p => p.lastBuildStatus === 'Succeeded');
  const other = projects.filter(p =>
    !p.lastBuildStatus ||
    !['Failed', 'Running', 'Pending', 'Succeeded'].includes(p.lastBuildStatus)
  );

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">Project Status</h1>
        <p className="text-gray-500 dark:text-slate-400">Overview of all project build statuses</p>
      </div>

      {/* Status Summary */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <StatusCard title="Failed" count={failed.length} color="error" />
        <StatusCard title="Building" count={building.length} color="warning" />
        <StatusCard title="Succeeded" count={succeeded.length} color="success" />
        <StatusCard title="Unknown" count={other.length} color="gray" />
      </div>

      {failed.length > 0 && <ProjectGroup title="Failed" projects={failed} variant="error" />}
      {building.length > 0 && <ProjectGroup title="Building" projects={building} variant="warning" />}
      {succeeded.length > 0 && <ProjectGroup title="Succeeded" projects={succeeded} variant="success" />}
      {other.length > 0 && <ProjectGroup title="No Builds Yet" projects={other} variant="default" />}
    </div>
  );
}

interface StatusCardProps {
  title: string;
  count: number;
  color: 'error' | 'warning' | 'success' | 'gray';
}

function StatusCard({ title, count, color }: StatusCardProps) {
  const colorClasses = {
    error: 'bg-error-50 border-error-200 text-error-700 dark:bg-error-500/10 dark:border-error-500/30 dark:text-error-400',
    warning: 'bg-warning-50 border-warning-200 text-warning-700 dark:bg-warning-500/10 dark:border-warning-500/30 dark:text-warning-400',
    success: 'bg-success-50 border-success-200 text-success-700 dark:bg-success-500/10 dark:border-success-500/30 dark:text-success-400',
    gray: 'bg-gray-50 border-gray-200 text-gray-700 dark:bg-slate-800 dark:border-slate-700 dark:text-slate-300',
  };

  return (
    <div className={`rounded-lg border p-4 ${colorClasses[color]}`}>
      <div className="text-sm font-medium">{title}</div>
      <div className="text-3xl font-bold">{count}</div>
    </div>
  );
}

interface ProjectGroupProps {
  title: string;
  projects: Array<{
    id: number;
    repoFullName: string;
    lastBuildStatus: string | null;
    lastBuildAt: string | null;
    isConfigured: boolean;
  }>;
  variant: 'error' | 'warning' | 'success' | 'default';
}

function ProjectGroup({ title, projects, variant }: ProjectGroupProps) {
  const headerColors = {
    error: 'border-error-200 bg-error-50 dark:border-error-500/30 dark:bg-error-500/10',
    warning: 'border-warning-200 bg-warning-50 dark:border-warning-500/30 dark:bg-warning-500/10',
    success: 'border-success-200 bg-success-50 dark:border-success-500/30 dark:bg-success-500/10',
    default: 'border-gray-200 bg-gray-50 dark:border-slate-700 dark:bg-slate-800',
  };

  return (
    <div className="bg-white border border-gray-200 rounded-xl overflow-hidden dark:bg-slate-900 dark:border-slate-800">
      <div className={`px-4 py-3 border-b ${headerColors[variant]}`}>
        <h2 className="font-medium text-gray-900 dark:text-slate-100">{title} ({projects.length})</h2>
      </div>
      <div className="divide-y divide-gray-200 dark:divide-slate-700">
        {projects.map((project) => (
          <Link
            key={project.id}
            to={`/projects/${project.id}`}
            className="block px-4 py-3 hover:bg-gray-50 dark:hover:bg-slate-800"
          >
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-medium text-primary-600 dark:text-primary-400">
                  {project.repoFullName}
                </p>
                {project.lastBuildAt && (
                  <p className="text-xs text-gray-500 dark:text-slate-400">
                    Last build: {formatRelativeTime(project.lastBuildAt)}
                  </p>
                )}
              </div>
              <div className="flex items-center space-x-2">
                {!project.isConfigured && (
                  <Badge variant="warning">Unconfigured</Badge>
                )}
                {project.lastBuildStatus && (
                  <Badge variant={getBuildStatusVariant(project.lastBuildStatus)}>
                    {project.lastBuildStatus}
                  </Badge>
                )}
              </div>
            </div>
          </Link>
        ))}
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
