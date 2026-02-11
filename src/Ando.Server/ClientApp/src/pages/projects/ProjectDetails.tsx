// =============================================================================
// pages/projects/ProjectDetails.tsx
//
// Project details page showing project info and build history.
// =============================================================================

import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useParams, Link, useNavigate } from 'react-router-dom';
import { getProject, triggerBuild } from '@/api/projects';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge, getBuildStatusVariant } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';

export function ProjectDetails() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const { data, isLoading, error } = useQuery({
    queryKey: ['project', id],
    queryFn: () => getProject(Number(id)),
    enabled: !!id,
  });

  const triggerMutation = useMutation({
    mutationFn: () => triggerBuild(Number(id)),
    onSuccess: (result) => {
      if (result.buildId) {
        navigate(`/builds/${result.buildId}`);
      }
      queryClient.invalidateQueries({ queryKey: ['project', id] });
    },
  });

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading project..." />;
  }

  if (error) {
    return <Alert variant="error">Failed to load project</Alert>;
  }

  const project = data?.project;

  if (!project) {
    return <Alert variant="error">Project not found</Alert>;
  }

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-start">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-50">{project.repoFullName}</h1>
          <p className="text-gray-500 dark:text-slate-400">{project.defaultBranch}</p>
        </div>
        <div className="flex space-x-3">
          <Link to={`/projects/${id}/settings`}>
            <Button variant="secondary">Settings</Button>
          </Link>
          <Button
            onClick={() => triggerMutation.mutate()}
            isLoading={triggerMutation.isPending}
          >
            Trigger Build
          </Button>
        </div>
      </div>

      {/* Configuration Warning */}
      {!project.isConfigured && (
        <Alert variant="warning">
          This project is missing {project.missingSecretsCount} required secrets.{' '}
          <Link to={`/projects/${id}/settings`} className="underline">
            Configure secrets
          </Link>
        </Alert>
      )}

      {/* Stats */}
      <div className="grid grid-cols-1 md:grid-cols-4 gap-4">
        <StatCard title="Total Builds" value={project.totalBuilds} />
        <StatCard title="Successful" value={project.successfulBuilds} className="text-success-600 dark:text-success-400" />
        <StatCard title="Failed" value={project.failedBuilds} className="text-error-600 dark:text-error-400" />
        <StatCard
          title="Success Rate"
          value={project.totalBuilds > 0
            ? `${Math.round((project.successfulBuilds / project.totalBuilds) * 100)}%`
            : 'N/A'
          }
        />
      </div>

      {/* Recent Builds */}
      <div className="bg-white shadow rounded-lg dark:bg-slate-900">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-slate-700">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-50">Recent Builds</h2>
        </div>
        <div className="divide-y divide-gray-200 dark:divide-slate-700">
          {project.recentBuilds.length === 0 ? (
            <div className="px-4 py-8 text-center text-gray-500 dark:text-slate-400">
              No builds yet. Trigger a build to get started.
            </div>
          ) : (
            project.recentBuilds.map((build) => (
              <Link
                key={build.id}
                to={`/builds/${build.id}`}
                className="block px-4 py-4 hover:bg-gray-50 dark:hover:bg-slate-800"
              >
                <div className="flex items-center justify-between">
                  <div className="flex-1 min-w-0">
                    <p className="text-sm font-medium text-gray-900 dark:text-slate-100">
                      Build #{build.id}
                    </p>
                    <p className="text-sm text-gray-500 dark:text-slate-400">
                      {build.branch} · {build.shortCommitSha}
                      {build.commitMessage && ` · ${build.commitMessage}`}
                    </p>
                  </div>
                  <div className="flex items-center space-x-4">
                    <Badge variant={getBuildStatusVariant(build.status)}>
                      {build.status}
                    </Badge>
                    {build.duration && (
                      <span className="text-sm text-gray-500 dark:text-slate-400">
                        {formatDuration(build.duration)}
                      </span>
                    )}
                    <span className="text-sm text-gray-500 dark:text-slate-400">
                      {formatDate(build.queuedAt)}
                    </span>
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

function StatCard({ title, value, className = '' }: { title: string; value: number | string; className?: string }) {
  return (
    <div className="bg-white shadow rounded-lg px-4 py-5 sm:p-6 dark:bg-slate-900">
      <dt className="text-sm font-medium text-gray-500 truncate dark:text-slate-400">{title}</dt>
      <dd className={`mt-1 text-3xl font-semibold text-gray-900 dark:text-slate-50 ${className}`}>{value}</dd>
    </div>
  );
}

function formatDuration(duration: string): string {
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

function formatDate(dateStr: string): string {
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
