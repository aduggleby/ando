// =============================================================================
// pages/projects/ProjectList.tsx
//
// Projects list page showing all user's projects.
// =============================================================================

import { useQuery } from '@tanstack/react-query';
import { Link } from 'react-router-dom';
import { getProjects } from '@/api/projects';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge, getBuildStatusVariant } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';

export function ProjectList() {
  const { data, isLoading, error } = useQuery({
    queryKey: ['projects'],
    queryFn: getProjects,
  });

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
        <h1 className="text-2xl font-bold text-gray-900">Projects</h1>
        <Link to="/projects/create">
          <Button>Add Project</Button>
        </Link>
      </div>

      {projects.length === 0 ? (
        <div className="bg-white shadow rounded-lg px-4 py-12 text-center">
          <p className="text-gray-500 mb-4">No projects yet.</p>
          <Link to="/projects/create">
            <Button>Create your first project</Button>
          </Link>
        </div>
      ) : (
        <div className="bg-white shadow overflow-hidden rounded-lg">
          <ul className="divide-y divide-gray-200">
            {projects.map((project) => (
              <li key={project.id}>
                <Link
                  to={`/projects/${project.id}`}
                  className="block hover:bg-gray-50"
                >
                  <div className="px-4 py-4 sm:px-6">
                    <div className="flex items-center justify-between">
                      <div className="flex-1 min-w-0">
                        <p className="text-sm font-medium text-primary-600 truncate">
                          {project.repoFullName}
                        </p>
                        <p className="text-sm text-gray-500">
                          {project.totalBuilds} builds
                          {project.lastBuildAt && (
                            <> · Last build {formatDate(project.lastBuildAt)}</>
                          )}
                        </p>
                      </div>
                      <div className="flex items-center space-x-4">
                        {!project.isConfigured && (
                          <Badge variant="warning">
                            {project.missingSecretsCount} secrets missing
                          </Badge>
                        )}
                        {project.lastBuildStatus && (
                          <Badge variant={getBuildStatusVariant(project.lastBuildStatus)}>
                            {project.lastBuildStatus}
                          </Badge>
                        )}
                      </div>
                    </div>
                  </div>
                </Link>
              </li>
            ))}
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
