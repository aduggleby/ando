// =============================================================================
// pages/Home.tsx
//
// Dashboard with two-column layout: recent builds (left) + project health (right).
// Merges the former ProjectStatus page into the main dashboard.
// =============================================================================

import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { getDashboard } from '@/api/dashboard';
import { getProjects } from '@/api/projects';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge, getBuildStatusVariant } from '@/components/ui/Badge';
import type { ProjectListItemDto } from '@/types';

export function Home() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['dashboard'],
    queryFn: getDashboard,
  });

  const { data: projectsData } = useQuery({
    queryKey: ['projects'],
    queryFn: getProjects,
    refetchInterval: 30000,
  });

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading dashboard..." />;
  }

  if (error) {
    return <Alert variant="error">Failed to load dashboard</Alert>;
  }

  const dashboard = data?.dashboard;
  const projects = projectsData?.projects || [];

  // Group projects by normalized status. "No Builds" is based on build count,
  // not unknown status strings, to avoid contradictory UI states.
  const noBuilds = projects.filter(p => p.totalBuilds === 0 || !p.lastBuildStatus);
  const withBuilds = projects.filter(p => p.totalBuilds > 0 && !!p.lastBuildStatus);

  const failed = withBuilds.filter(p => {
    const status = normalizeStatus(p.lastBuildStatus);
    return status === 'failed' || status === 'cancelled' || status === 'timedout';
  });

  const building = withBuilds.filter(p => {
    const status = normalizeStatus(p.lastBuildStatus);
    return status === 'running' || status === 'queued' || status === 'pending';
  });

  const succeeded = withBuilds.filter(p => {
    const status = normalizeStatus(p.lastBuildStatus);
    return status === 'success' || status === 'succeeded';
  });

  const other = withBuilds.filter(p => {
    const status = normalizeStatus(p.lastBuildStatus);
    return !['failed', 'cancelled', 'timedout', 'running', 'queued', 'pending', 'success', 'succeeded'].includes(status);
  });

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight">
        Dashboard
      </h1>

      {/* Statistics */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <StatCard
          title="Projects"
          value={dashboard?.totalProjects || 0}
          glow="cyan"
        />
        <StatCard
          title="Builds Today"
          value={dashboard?.buildsToday || 0}
          glow="green"
        />
        <StatCard
          title="Failed Today"
          value={dashboard?.failedToday || 0}
          glow="red"
          danger={!!dashboard?.failedToday}
        />
        <StatCard
          title="Success Rate"
          value={projects.length > 0
            ? `${Math.round((succeeded.length / projects.length) * 100)}%`
            : '—'
          }
          glow="green"
        />
      </div>

      {/* Two-column layout */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Left: Recent Builds (2/3 width) */}
        <div className="lg:col-span-2">
          <div className="bg-white border border-gray-200 rounded-xl overflow-hidden dark:bg-slate-900 dark:border-slate-800">
            <div className="px-5 py-4 border-b border-gray-100 dark:border-slate-800 flex items-center justify-between">
              <h2 className="text-sm font-semibold text-gray-900 dark:text-slate-100">Recent Builds</h2>
              {dashboard?.recentBuilds && dashboard.recentBuilds.length > 0 && (
                <span className="text-[11px] font-medium text-primary-600 bg-primary-50 dark:text-primary-400 dark:bg-primary-500/10 px-2.5 py-0.5 rounded-full">
                  {dashboard.recentBuilds.length} builds
                </span>
              )}
            </div>
            <div className="divide-y divide-gray-100 dark:divide-slate-800/50">
              {dashboard?.recentBuilds.length === 0 ? (
                <div className="px-5 py-10 text-center text-gray-400 dark:text-slate-500 text-sm">
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
                    className="flex items-center gap-4 px-5 py-3.5 hover:bg-gray-50 dark:hover:bg-slate-800/50 transition-colors"
                  >
                    <div className="flex-1 min-w-0">
                      <p className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate">
                        {build.projectName}
                      </p>
                      <div className="flex items-center gap-2 mt-0.5">
                        {build.gitVersionTag && (
                          <span className="text-[11px] font-medium font-mono text-primary-600 bg-primary-50 dark:text-primary-400 dark:bg-primary-500/10 px-1.5 py-0.5 rounded">
                            {build.gitVersionTag}
                          </span>
                        )}
                        <span className="text-xs text-gray-400 dark:text-slate-500 font-mono">
                          {build.branch}
                          <span className="text-gray-300 dark:text-slate-600"> @</span>
                          {build.shortCommitSha}
                        </span>
                      </div>
                    </div>
                    <Badge variant={getBuildStatusVariant(build.status)}>
                      {build.status}
                    </Badge>
                    {build.duration && (
                      <span className="text-xs font-mono text-gray-400 dark:text-slate-500 min-w-[56px] text-right">
                        {formatDuration(build.duration)}
                      </span>
                    )}
                  </Link>
                ))
              )}
            </div>
          </div>
        </div>

        {/* Right: Project Health (1/3 width) */}
        <div className="space-y-4">
          <ProjectHealthGroup
            title="Failed"
            projects={failed}
            dotColor="bg-error-500"
            dotGlow="shadow-[0_0_6px_rgba(244,63,94,0.5)]"
          />
          <ProjectHealthGroup
            title="Building"
            projects={building}
            dotColor="bg-warning-500"
            dotGlow="shadow-[0_0_6px_rgba(245,158,11,0.5)]"
            pulse
          />
          <ProjectHealthGroup
            title="Succeeded"
            projects={succeeded}
            dotColor="bg-success-500"
            dotGlow="shadow-[0_0_6px_rgba(16,185,129,0.5)]"
          />
          {other.length > 0 && (
            <ProjectHealthGroup
              title="Other"
              projects={other}
              dotColor="bg-gray-400 dark:bg-slate-500"
              dotGlow=""
            />
          )}

          {noBuilds.length > 0 && (
            <ProjectHealthGroup
              title="No Builds"
              projects={noBuilds}
              dotColor="bg-gray-400 dark:bg-slate-500"
              dotGlow=""
            />
          )}

          {projects.length === 0 && (
            <div className="bg-white border border-gray-200 rounded-xl px-5 py-8 text-center dark:bg-slate-900 dark:border-slate-800">
              <p className="text-sm text-gray-400 dark:text-slate-500">No projects yet.</p>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function ProjectHealthGroup({
  title,
  projects,
  dotColor,
  dotGlow,
  pulse = false,
}: {
  title: string;
  projects: ProjectListItemDto[];
  dotColor: string;
  dotGlow: string;
  pulse?: boolean;
}) {
  if (projects.length === 0) return null;

  return (
    <div className="bg-white border border-gray-200 rounded-xl overflow-hidden dark:bg-slate-900 dark:border-slate-800">
      <div className="px-4 py-3 border-b border-gray-100 dark:border-slate-800 flex items-center justify-between">
        <h3 className="text-xs font-semibold text-gray-900 dark:text-slate-100 uppercase tracking-wider">{title}</h3>
        <span className="text-[11px] font-medium text-gray-400 dark:text-slate-500">{projects.length}</span>
      </div>
      <div className="divide-y divide-gray-100 dark:divide-slate-800/50">
        {projects.map((project) => (
          <Link
            key={project.id}
            to={`/projects/${project.id}`}
            className="flex items-center gap-3 px-4 py-2.5 hover:bg-gray-50 dark:hover:bg-slate-800/50 transition-colors"
          >
            <span className={`w-1.5 h-1.5 rounded-full shrink-0 ${dotColor} ${dotGlow} ${pulse ? 'animate-pulse' : ''}`} />
            <span className="text-sm text-gray-700 dark:text-slate-300 truncate">{project.repoFullName}</span>
            {!project.isConfigured && (
              <span className="text-[10px] text-warning-600 dark:text-warning-400 shrink-0">unconfigured</span>
            )}
          </Link>
        ))}
      </div>
    </div>
  );
}

function StatCard({
  title,
  value,
  glow,
  danger = false,
}: {
  title: string;
  value: number | string;
  glow: 'cyan' | 'green' | 'red';
  danger?: boolean;
}) {
  const glowClass =
    glow === 'cyan' ? 'phosphor-glow-cyan' :
    glow === 'green' ? 'phosphor-glow-green' :
    'phosphor-glow-red';

  return (
    <div className={`
      relative overflow-hidden bg-white border border-gray-200 rounded-xl px-5 py-5
      dark:bg-slate-900 dark:border-slate-800
      transition-colors hover:border-gray-300 dark:hover:border-slate-700
      ${glowClass}
    `}>
      <dt className="text-xs font-medium text-gray-400 dark:text-slate-500 uppercase tracking-wider">
        {title}
      </dt>
      <dd className={`
        mt-2 text-3xl font-light tracking-tight
        ${danger
          ? 'text-error-500 dark:text-error-400'
          : 'text-gray-900 dark:text-slate-100'
        }
      `}>
        {value}
      </dd>
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

function normalizeStatus(status: string | null): string {
  return (status ?? '').trim().toLowerCase();
}
