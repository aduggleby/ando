// =============================================================================
// pages/projects/ProjectCreate.tsx
//
// Project creation page for adding new projects.
// =============================================================================

import { useState } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { useNavigate } from 'react-router-dom';
import { createProject } from '@/api/projects';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Alert } from '@/components/ui/Alert';

export function ProjectCreate() {
  const [repoUrl, setRepoUrl] = useState('');
  const [error, setError] = useState('');
  const navigate = useNavigate();
  const queryClient = useQueryClient();

  const createMutation = useMutation({
    mutationFn: () => createProject(repoUrl),
    onSuccess: (data) => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      if (data.project?.id) {
        navigate(`/projects/${data.project.id}`);
      } else {
        navigate('/projects');
      }
    },
    onError: () => {
      setError('Failed to create project. Please check the repository URL.');
    },
  });

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setError('');

    if (!repoUrl.trim()) {
      setError('Repository URL is required');
      return;
    }

    createMutation.mutate();
  };

  return (
    <div className="max-w-2xl mx-auto">
      <div className="space-y-6">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100 tracking-tight">Add Project</h1>
          <p className="mt-1 text-sm text-gray-400 dark:text-slate-500">
            Connect a GitHub repository to start building.
          </p>
        </div>

        {error && (
          <Alert variant="error">{error}</Alert>
        )}

        <div className="bg-white border border-gray-200 rounded-xl p-6 dark:bg-slate-900 dark:border-slate-800">
          <form onSubmit={handleSubmit} className="space-y-6">
            <Input
              label="GitHub Repository URL"
              type="url"
              placeholder="https://github.com/owner/repo"
              value={repoUrl}
              onChange={(e) => setRepoUrl(e.target.value)}
              helperText="Enter the full URL to your GitHub repository"
              required
            />

            <div className="bg-gray-50 border border-gray-100 rounded-lg p-4 dark:bg-slate-800 dark:border-slate-700">
              <h3 className="text-sm font-medium text-gray-900 mb-2 dark:text-slate-100">Requirements</h3>
              <ul className="text-xs text-gray-500 space-y-1.5 list-disc list-inside dark:text-slate-400">
                <li>Repository must be accessible by the Ando GitHub App</li>
                <li>Repository must contain a <code className="bg-gray-200 px-1 rounded text-[11px] dark:bg-slate-700">build.csando</code> file</li>
                <li>Repository must have webhooks enabled</li>
              </ul>
            </div>

            <div className="flex justify-end gap-2">
              <Button
                type="button"
                variant="secondary"
                onClick={() => navigate('/projects')}
              >
                Cancel
              </Button>
              <Button
                type="submit"
                isLoading={createMutation.isPending}
              >
                Add Project
              </Button>
            </div>
          </form>
        </div>
      </div>
    </div>
  );
}
