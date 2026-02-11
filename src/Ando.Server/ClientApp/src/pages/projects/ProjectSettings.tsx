// =============================================================================
// pages/projects/ProjectSettings.tsx
//
// Project settings page for managing secrets and configuration.
// =============================================================================

import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useParams, Link, useNavigate } from 'react-router-dom';
import {
  getProjectSettings,
  setSecret,
  deleteSecret,
  bulkImportSecrets,
  refreshSecrets,
  deleteProject,
} from '@/api/projects';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';

export function ProjectSettings() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [newSecretName, setNewSecretName] = useState('');
  const [newSecretValue, setNewSecretValue] = useState('');
  const [bulkSecrets, setBulkSecrets] = useState('');
  const [showBulkImport, setShowBulkImport] = useState(false);
  const [showDeleteConfirm, setShowDeleteConfirm] = useState(false);
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const { data, isLoading, error: loadError } = useQuery({
    queryKey: ['project-settings', id],
    queryFn: () => getProjectSettings(Number(id)),
    enabled: !!id,
  });

  const setSecretMutation = useMutation({
    mutationFn: ({ name, value }: { name: string; value: string }) =>
      setSecret(Number(id), name, value),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['project-settings', id] });
      setNewSecretName('');
      setNewSecretValue('');
      setSuccess('Secret saved successfully');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to save secret'),
  });

  const deleteSecretMutation = useMutation({
    mutationFn: (name: string) => deleteSecret(Number(id), name),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['project-settings', id] });
      setSuccess('Secret deleted');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to delete secret'),
  });

  const bulkImportMutation = useMutation({
    mutationFn: () => bulkImportSecrets(Number(id), bulkSecrets),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: ['project-settings', id] });
      setBulkSecrets('');
      setShowBulkImport(false);
      setSuccess(`Imported ${result.importedCount} secrets`);
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to import secrets'),
  });

  const refreshMutation = useMutation({
    mutationFn: () => refreshSecrets(Number(id)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['project-settings', id] });
      setSuccess('Required secrets refreshed');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to refresh secrets'),
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteProject(Number(id)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['projects'] });
      navigate('/projects');
    },
    onError: () => setError('Failed to delete project'),
  });

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading settings..." />;
  }

  if (loadError) {
    return <Alert variant="error">Failed to load project settings</Alert>;
  }

  const settings = data?.settings;

  if (!settings) {
    return <Alert variant="error">Project not found</Alert>;
  }

  const handleAddSecret = (e: React.FormEvent) => {
    e.preventDefault();
    setError('');
    if (!newSecretName.trim() || !newSecretValue.trim()) {
      setError('Both name and value are required');
      return;
    }
    setSecretMutation.mutate({ name: newSecretName, value: newSecretValue });
  };

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-50">Project Settings</h1>
          <p className="text-gray-500 dark:text-slate-400">{settings.repoFullName}</p>
        </div>
        <Link to={`/projects/${id}`}>
          <Button variant="secondary">Back to Project</Button>
        </Link>
      </div>

      {error && <Alert variant="error">{error}</Alert>}
      {success && <Alert variant="success">{success}</Alert>}

      {/* Required Secrets */}
      <div className="bg-white shadow rounded-lg dark:bg-slate-900">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200 flex justify-between items-center dark:border-slate-700">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-50">Required Secrets</h2>
          <Button
            variant="ghost"
            size="sm"
            onClick={() => refreshMutation.mutate()}
            isLoading={refreshMutation.isPending}
          >
            Refresh
          </Button>
        </div>
        <div className="divide-y divide-gray-200 dark:divide-slate-700">
          {settings.requiredSecrets.length === 0 ? (
            <div className="px-4 py-8 text-center text-gray-500 dark:text-slate-400">
              No secrets required. The build script doesn't use any secret variables.
            </div>
          ) : (
            settings.requiredSecrets.map((secret) => (
              <div key={secret.name} className="px-4 py-4 flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium text-gray-900 dark:text-slate-100">{secret.name}</p>
                  <p className={`text-sm ${secret.isSet ? 'text-success-600 dark:text-success-400' : 'text-error-600 dark:text-error-400'}`}>
                    {secret.isSet ? 'Configured' : 'Not configured'}
                  </p>
                </div>
                {secret.isSet && (
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => {
                      if (confirm(`Delete secret "${secret.name}"?`)) {
                        deleteSecretMutation.mutate(secret.name);
                      }
                    }}
                  >
                    Delete
                  </Button>
                )}
              </div>
            ))
          )}
        </div>
      </div>

      {/* Add Secret */}
      <div className="bg-white shadow rounded-lg p-6 dark:bg-slate-900">
        <h2 className="text-lg font-medium text-gray-900 mb-4 dark:text-slate-50">Add Secret</h2>
        <form onSubmit={handleAddSecret} className="space-y-4">
          <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
            <Input
              label="Secret Name"
              placeholder="MY_SECRET"
              value={newSecretName}
              onChange={(e) => setNewSecretName(e.target.value.toUpperCase())}
            />
            <Input
              label="Secret Value"
              type="password"
              placeholder="Value"
              value={newSecretValue}
              onChange={(e) => setNewSecretValue(e.target.value)}
            />
          </div>
          <div className="flex justify-between">
            <Button
              type="button"
              variant="ghost"
              onClick={() => setShowBulkImport(!showBulkImport)}
            >
              {showBulkImport ? 'Hide Bulk Import' : 'Bulk Import'}
            </Button>
            <Button type="submit" isLoading={setSecretMutation.isPending}>
              Add Secret
            </Button>
          </div>
        </form>

        {/* Bulk Import */}
        {showBulkImport && (
          <div className="mt-6 pt-6 border-t border-gray-200 dark:border-slate-700">
            <h3 className="text-sm font-medium text-gray-900 mb-2 dark:text-slate-100">Bulk Import</h3>
            <p className="text-sm text-gray-500 mb-4 dark:text-slate-400">
              Paste environment variables in KEY=value format, one per line.
            </p>
            <textarea
              className="w-full h-32 px-3 py-2 border border-gray-300 rounded-md focus:outline-none focus:ring-2 focus:ring-primary-500 font-mono text-sm dark:bg-slate-800 dark:border-slate-600 dark:text-slate-100 dark:placeholder-slate-400"
              placeholder="SECRET_KEY=value&#10;ANOTHER_SECRET=another_value"
              value={bulkSecrets}
              onChange={(e) => setBulkSecrets(e.target.value)}
            />
            <div className="mt-4 flex justify-end">
              <Button
                onClick={() => bulkImportMutation.mutate()}
                isLoading={bulkImportMutation.isPending}
              >
                Import Secrets
              </Button>
            </div>
          </div>
        )}
      </div>

      {/* All Secrets */}
      <div className="bg-white shadow rounded-lg dark:bg-slate-900">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-slate-700">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-50">All Secrets</h2>
        </div>
        <div className="divide-y divide-gray-200 dark:divide-slate-700">
          {settings.allSecrets.length === 0 ? (
            <div className="px-4 py-8 text-center text-gray-500 dark:text-slate-400">
              No secrets configured yet.
            </div>
          ) : (
            settings.allSecrets.map((secret) => (
              <div key={secret.name} className="px-4 py-4 flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium text-gray-900 dark:text-slate-100">{secret.name}</p>
                  <p className="text-xs text-gray-500 dark:text-slate-400">
                    Last updated: {new Date(secret.updatedAt).toLocaleDateString()}
                  </p>
                </div>
                <Button
                  variant="ghost"
                  size="sm"
                  onClick={() => {
                    if (confirm(`Delete secret "${secret.name}"?`)) {
                      deleteSecretMutation.mutate(secret.name);
                    }
                  }}
                >
                  Delete
                </Button>
              </div>
            ))
          )}
        </div>
      </div>

      {/* Danger Zone */}
      <div className="bg-white shadow rounded-lg border border-error-200 dark:bg-slate-900 dark:border-error-500/30">
        <div className="px-4 py-5 sm:px-6 border-b border-error-200 dark:border-error-500/30">
          <h2 className="text-lg font-medium text-error-600 dark:text-error-400">Danger Zone</h2>
        </div>
        <div className="p-6">
          {showDeleteConfirm ? (
            <div className="space-y-4">
              <p className="text-sm text-gray-700 dark:text-slate-300">
                Are you sure you want to delete this project? This action cannot be undone.
                All build history and secrets will be permanently deleted.
              </p>
              <div className="flex space-x-3">
                <Button
                  variant="danger"
                  onClick={() => deleteMutation.mutate()}
                  isLoading={deleteMutation.isPending}
                >
                  Yes, Delete Project
                </Button>
                <Button variant="secondary" onClick={() => setShowDeleteConfirm(false)}>
                  Cancel
                </Button>
              </div>
            </div>
          ) : (
            <div className="flex items-center justify-between">
              <div>
                <p className="text-sm font-medium text-gray-900 dark:text-slate-100">Delete this project</p>
                <p className="text-sm text-gray-500 dark:text-slate-400">
                  Permanently delete this project and all associated data.
                </p>
              </div>
              <Button variant="danger" onClick={() => setShowDeleteConfirm(true)}>
                Delete Project
              </Button>
            </div>
          )}
        </div>
      </div>
    </div>
  );
}
