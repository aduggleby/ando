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

  const latestBuild = project.recentBuilds[0];

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-start">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight">{project.repoFullName}</h1>
          <p className="text-sm text-gray-400 dark:text-slate-500 mt-0.5">{project.defaultBranch}</p>
          <p className="text-xs text-gray-400 dark:text-slate-500 mt-1 font-mono">
            {latestBuild?.gitVersionTag
              ? <span className="text-primary-600 dark:text-primary-400">{latestBuild.gitVersionTag}</span>
              : '—'
            }
          </p>
        </div>
        <div className="flex gap-2">
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
        <StatCard title="Successful" value={project.successfulBuilds} valueClass="text-success-600 dark:text-success-400" />
        <StatCard title="Failed" value={project.failedBuilds} valueClass="text-error-600 dark:text-error-400" />
        <StatCard
          title="Success Rate"
          value={project.totalBuilds > 0
            ? `${Math.round((project.successfulBuilds / project.totalBuilds) * 100)}%`
            : 'N/A'
          }
        />
      </div>

      {/* Recent Builds */}
      <div className="bg-white border border-gray-200 rounded-xl overflow-hidden dark:bg-slate-900 dark:border-slate-800">
        <div className="px-5 py-4 border-b border-gray-100 dark:border-slate-800">
          <h2 className="text-sm font-semibold text-gray-900 dark:text-slate-100">Recent Builds</h2>
        </div>
        <div className="divide-y divide-gray-100 dark:divide-slate-800/50">
          {project.recentBuilds.length === 0 ? (
            <div className="px-5 py-10 text-center text-gray-400 dark:text-slate-500 text-sm">
              No builds yet. Trigger a build to get started.
            </div>
          ) : (
            project.recentBuilds.map((build) => (
              <Link
                key={build.id}
                to={`/builds/${build.id}`}
                className="flex items-center gap-4 px-5 py-3.5 hover:bg-gray-50 dark:hover:bg-slate-800/50 transition-colors"
              >
                <div className="flex-1 min-w-0">
                  <p className="text-sm font-semibold text-gray-900 dark:text-slate-100">
                    Build #{build.id}
                  </p>
                  <p className="text-xs text-gray-400 dark:text-slate-500 font-mono mt-0.5">
                    {build.branch}@{build.shortCommitSha}
                    {build.commitMessage && <span className="text-gray-400 dark:text-slate-500 font-sans"> · {build.commitMessage}</span>}
                  </p>
                </div>
                <Badge variant={getBuildStatusVariant(build.status)}>
                  {build.status}
                </Badge>
                {build.duration && (
                  <span className="text-xs font-mono text-gray-400 dark:text-slate-500 min-w-[56px] text-right">
                    {formatDuration(build.duration)}
                  </span>
                )}
                <span className="text-xs text-gray-400 dark:text-slate-500 min-w-[60px] text-right">
                  {formatDate(build.queuedAt)}
                </span>
              </Link>
            ))
          )}
        </div>
      </div>
    </div>
  );
}

function StatCard({ title, value, valueClass = '' }: { title: string; value: number | string; valueClass?: string }) {
  return (
    <div className="bg-white border border-gray-200 rounded-xl px-5 py-5 dark:bg-slate-900 dark:border-slate-800">
      <dt className="text-xs font-medium text-gray-400 dark:text-slate-500 uppercase tracking-wider">{title}</dt>
      <dd className={`mt-2 text-2xl font-light text-gray-900 dark:text-slate-100 ${valueClass}`}>{value}</dd>
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
