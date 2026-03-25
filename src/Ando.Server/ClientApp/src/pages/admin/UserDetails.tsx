// =============================================================================
// pages/admin/UserDetails.tsx
//
// Admin user details page.
// =============================================================================

import { useState } from 'react';
import { Link, useNavigate, useParams } from 'react-router-dom';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import {
  getUserDetails,
  changeUserRole,
  lockUser,
  unlockUser,
  deleteUser,
  impersonateUser,
} from '@/api/admin';
import { useAuth } from '@/context/AuthContext';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';

export function UserDetails() {
  const { id } = useParams<{ id: string }>();
  const userId = Number(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { user: currentUser, refreshUser } = useAuth();
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const { data, isLoading, error: loadError } = useQuery({
    queryKey: ['admin-user', userId],
    queryFn: () => getUserDetails(userId),
    enabled: Number.isFinite(userId) && userId > 0,
  });

  const changeRoleMutation = useMutation({
    mutationFn: (isAdmin: boolean) => changeUserRole(userId, isAdmin),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-user', userId] });
      queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      setSuccess('User role updated');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to change user role'),
  });

  const lockMutation = useMutation({
    mutationFn: () => lockUser(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-user', userId] });
      queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      setSuccess('User locked');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to lock user'),
  });

  const unlockMutation = useMutation({
    mutationFn: () => unlockUser(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-user', userId] });
      queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      setSuccess('User unlocked');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to unlock user'),
  });

  const deleteMutation = useMutation({
    mutationFn: () => deleteUser(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      navigate('/admin/users');
    },
    onError: () => setError('Failed to delete user'),
  });

  const impersonateMutation = useMutation({
    mutationFn: () => impersonateUser(userId),
    onSuccess: async () => {
      await refreshUser();
      navigate('/');
    },
    onError: () => setError('Failed to impersonate user'),
  });

  if (!Number.isFinite(userId) || userId <= 0) {
    return <Alert variant="error">Invalid user ID</Alert>;
  }

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading user details..." />;
  }

  if (loadError || !data?.user) {
    return <Alert variant="error">Failed to load user details</Alert>;
  }

  const user = data.user;
  const isCurrentUser = user.id === currentUser?.id;

  return (
    <div className="space-y-6">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">User Details</h1>
          <p className="text-gray-500 dark:text-slate-400">{user.email}</p>
        </div>
        <Link to="/admin/users">
          <Button variant="secondary">Back to Users</Button>
        </Link>
      </div>

      {error && <Alert variant="error">{error}</Alert>}
      {success && <Alert variant="success">{success}</Alert>}

      <div className="bg-white border border-gray-200 rounded-xl p-6 dark:bg-slate-900 dark:border-slate-800">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <InfoItem label="Display Name" value={user.displayName || '-'} />
          <InfoItem label="Email" value={user.email} />
          <InfoItem label="Created" value={new Date(user.createdAt).toLocaleString()} />
          <InfoItem label="Last Login" value={user.lastLoginAt ? new Date(user.lastLoginAt).toLocaleString() : 'Never'} />
          <InfoItem label="GitHub" value={user.hasGitHubConnection ? (user.gitHubLogin || 'Connected') : 'Not Connected'} />
          <InfoItem label="Total Builds" value={String(user.totalBuilds)} />
        </div>

        <div className="mt-6 flex flex-wrap items-center gap-2">
          <Badge variant={user.isAdmin ? 'primary' : 'default'}>{user.isAdmin ? 'Admin' : 'User'}</Badge>
          <Badge variant={user.emailVerified ? 'success' : 'warning'}>{user.emailVerified ? 'Verified' : 'Unverified'}</Badge>
          {user.isLockedOut && <Badge variant="error">Locked</Badge>}
        </div>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl p-6 dark:bg-slate-900 dark:border-slate-800">
        <h2 className="text-lg font-medium text-gray-900 dark:text-slate-100 mb-4">Actions</h2>
        <div className="flex flex-wrap gap-3">
          {!isCurrentUser && (
            <>
              <Button
                variant="secondary"
                onClick={() => impersonateMutation.mutate()}
                isLoading={impersonateMutation.isPending}
              >
                Impersonate
              </Button>
              <Button
                variant="secondary"
                onClick={() => changeRoleMutation.mutate(!user.isAdmin)}
                isLoading={changeRoleMutation.isPending}
              >
                {user.isAdmin ? 'Demote to User' : 'Promote to Admin'}
              </Button>
              {user.isLockedOut ? (
                <Button
                  variant="secondary"
                  onClick={() => unlockMutation.mutate()}
                  isLoading={unlockMutation.isPending}
                >
                  Unlock User
                </Button>
              ) : (
                <Button
                  variant="secondary"
                  onClick={() => lockMutation.mutate()}
                  isLoading={lockMutation.isPending}
                >
                  Lock User
                </Button>
              )}
              <Button
                variant="danger"
                onClick={() => {
                  if (confirm(`Delete user "${user.email}"? This cannot be undone.`)) {
                    deleteMutation.mutate();
                  }
                }}
                isLoading={deleteMutation.isPending}
              >
                Delete User
              </Button>
            </>
          )}
          {isCurrentUser && (
            <p className="text-sm text-gray-500 dark:text-slate-400">
              You cannot modify your own account from this page.
            </p>
          )}
        </div>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl dark:bg-slate-900 dark:border-slate-800">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-slate-800">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-100">Projects</h2>
        </div>
        <div className="divide-y divide-gray-100 dark:divide-slate-800">
          {user.projects.length === 0 ? (
            <div className="px-4 py-8 text-center text-gray-500 dark:text-slate-400">No projects</div>
          ) : (
            user.projects.map((project) => (
              <Link
                key={project.id}
                to={`/projects/${project.id}`}
                className="block px-4 py-4 hover:bg-gray-50 dark:hover:bg-slate-800"
              >
                <div className="flex items-center justify-between">
                  <div>
                    <p className="text-sm font-medium text-primary-600 dark:text-primary-400">{project.name}</p>
                    <p className="text-xs text-gray-500 dark:text-slate-400">
                      Created {new Date(project.createdAt).toLocaleDateString()}
                    </p>
                  </div>
                  <p className="text-sm text-gray-600 dark:text-slate-300">{project.buildCount} builds</p>
                </div>
              </Link>
            ))
          )}
        </div>
      </div>
    </div>
  );
}

function InfoItem({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-sm text-gray-500 dark:text-slate-400">{label}</p>
      <p className="text-sm font-medium text-gray-900 dark:text-slate-100">{value}</p>
    </div>
  );
}
