// =============================================================================
// pages/projects/ProjectList.tsx
//
// Projects list page showing all user's projects.
// =============================================================================

import { useEffect } from 'react';
import { useQuery, useQueryClient } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { getProjects } from '@/api/projects';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';

export function ProjectList() {
  const queryClient = useQueryClient();
  const { data, isLoading, error } = useQuery({
    queryKey: ['projects'],
    queryFn: getProjects,
  });

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/build-logs')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.on('BuildQueued', () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
    });

    connection.start().catch(() => {});

    return () => {
      connection.stop().catch(() => {});
    };
  }, [queryClient]);

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading projects..." />;
  }

  if (error) {
    return <Alert variant="error">Failed to load projects</Alert>;
  }

  const projects = data?.projects || [];

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight">Projects</h1>
        <Link to="/projects/create">
          <Button>Add Project</Button>
        </Link>
      </div>

      {projects.length === 0 ? (
        <div className="bg-white border border-gray-200 rounded-xl px-4 py-12 text-center dark:bg-slate-900 dark:border-slate-800">
          <p className="text-gray-400 dark:text-slate-500 mb-4">No projects yet.</p>
          <Link to="/projects/create">
            <Button>Create your first project</Button>
          </Link>
        </div>
      ) : (
        <div className="bg-white border border-gray-200 rounded-xl overflow-hidden dark:bg-slate-900 dark:border-slate-800">
          <ul className="divide-y divide-gray-100 dark:divide-slate-800/50">
            {projects.map((project) => {
              const status = getProjectStatusBadge(project);
              return (
                <li key={project.id}>
                  <Link
                    to={`/projects/${project.id}`}
                    className="block hover:bg-gray-50 dark:hover:bg-slate-800/50 transition-colors"
                  >
                    <div className="px-5 py-4">
                      <div className="flex items-center justify-between">
                        <div className="flex-1 min-w-0">
                          <div className="flex items-center gap-2.5">
                            <p className="text-sm font-semibold text-gray-900 dark:text-slate-100 truncate">
                              {project.repoFullName}
                            </p>
                            <Badge variant={status.variant}>{status.label}</Badge>
                          </div>
                          <p className="text-xs text-gray-400 dark:text-slate-500 mt-1">
                            {project.totalBuilds} builds
                            {project.lastBuildAt && (
                              <> · Last build {formatDate(project.lastBuildAt)}</>
                            )}
                            {project.lastBuildGitVersionTag && (
                              <>
                                {' · '}
                                <span className="font-mono text-primary-600 dark:text-primary-400">
                                  {project.lastBuildGitVersionTag}
                                </span>
                              </>
                            )}
                          </p>
                        </div>
                      </div>
                    </div>
                  </Link>
                </li>
              );
            })}
          </ul>
        </div>
      )}
    </div>
  );
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

function getProjectStatusBadge(project: {
  isConfigured: boolean;
  lastBuildStatus?: string | null;
}): { label: 'Secrets missing' | 'Failed' | 'Success'; variant: 'warning' | 'error' | 'success' } {
  if (!project.isConfigured) {
    return { label: 'Secrets missing', variant: 'warning' };
  }

  if (project.lastBuildStatus === 'Failed' || project.lastBuildStatus === 'TimedOut' || project.lastBuildStatus === 'Cancelled') {
    return { label: 'Failed', variant: 'error' };
  }

  return { label: 'Success', variant: 'success' };
}
