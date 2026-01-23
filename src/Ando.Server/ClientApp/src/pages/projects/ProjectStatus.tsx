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
        <h1 className="text-2xl font-bold text-gray-900">Project Status</h1>
        <p className="text-gray-500">Overview of all project build statuses</p>
      </div>

      {/* Status Summary */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <StatusCard
          title="Failed"
          count={failed.length}
          color="error"
        />
        <StatusCard
          title="Building"
          count={building.length}
          color="warning"
        />
        <StatusCard
          title="Succeeded"
          count={succeeded.length}
          color="success"
        />
        <StatusCard
          title="Unknown"
          count={other.length}
          color="gray"
        />
      </div>

      {/* Failed Projects */}
      {failed.length > 0 && (
        <ProjectGroup
          title="Failed"
          projects={failed}
          variant="error"
        />
      )}

      {/* Building Projects */}
      {building.length > 0 && (
        <ProjectGroup
          title="Building"
          projects={building}
          variant="warning"
        />
      )}

      {/* Succeeded Projects */}
      {succeeded.length > 0 && (
        <ProjectGroup
          title="Succeeded"
          projects={succeeded}
          variant="success"
        />
      )}

      {/* Other Projects */}
      {other.length > 0 && (
        <ProjectGroup
          title="No Builds Yet"
          projects={other}
          variant="default"
        />
      )}
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
    error: 'bg-error-50 border-error-200 text-error-700',
    warning: 'bg-warning-50 border-warning-200 text-warning-700',
    success: 'bg-success-50 border-success-200 text-success-700',
    gray: 'bg-gray-50 border-gray-200 text-gray-700',
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
    error: 'border-error-200 bg-error-50',
    warning: 'border-warning-200 bg-warning-50',
    success: 'border-success-200 bg-success-50',
    default: 'border-gray-200 bg-gray-50',
  };

  return (
    <div className="bg-white shadow rounded-lg overflow-hidden">
      <div className={`px-4 py-3 border-b ${headerColors[variant]}`}>
        <h2 className="font-medium text-gray-900">{title} ({projects.length})</h2>
      </div>
      <div className="divide-y divide-gray-200">
        {projects.map((project) => (
          <Link
            key={project.id}
            to={`/projects/${project.id}`}
            className="block px-4 py-3 hover:bg-gray-50"
          >
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-medium text-primary-600">
                  {project.repoFullName}
                </p>
                {project.lastBuildAt && (
                  <p className="text-xs text-gray-500">
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
