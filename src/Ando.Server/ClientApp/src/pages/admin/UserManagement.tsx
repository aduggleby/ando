// =============================================================================
// pages/admin/UserManagement.tsx
//
// Admin user management page.
// =============================================================================

import { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { Link, useNavigate } from 'react-router-dom';
import {
  getUsers,
  changeUserRole,
  lockUser,
  unlockUser,
  deleteUser,
  impersonateUser,
} from '@/api/admin';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import { useAuth } from '@/context/AuthContext';

export function UserManagement() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const { user: currentUser, refreshUser } = useAuth();
  const [error, setError] = useState('');
  const [success, setSuccess] = useState('');

  const { data, isLoading, error: loadError } = useQuery({
    queryKey: ['admin-users'],
    queryFn: getUsers,
  });

  const changeRoleMutation = useMutation({
    mutationFn: ({ userId, isAdmin }: { userId: number; isAdmin: boolean }) =>
      changeUserRole(userId, isAdmin),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      setSuccess('User role updated');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to change user role'),
  });

  const lockMutation = useMutation({
    mutationFn: (userId: number) => lockUser(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      setSuccess('User locked');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to lock user'),
  });

  const unlockMutation = useMutation({
    mutationFn: (userId: number) => unlockUser(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      setSuccess('User unlocked');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to unlock user'),
  });

  const deleteMutation = useMutation({
    mutationFn: (userId: number) => deleteUser(userId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['admin-users'] });
      setSuccess('User deleted');
      setTimeout(() => setSuccess(''), 3000);
    },
    onError: () => setError('Failed to delete user'),
  });

  const impersonateMutation = useMutation({
    mutationFn: (userId: number) => impersonateUser(userId),
    onSuccess: async () => {
      await refreshUser();
      navigate('/');
    },
    onError: () => setError('Failed to impersonate user'),
  });

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading users..." />;
  }

  if (loadError) {
    return <Alert variant="error">Failed to load users</Alert>;
  }

  const users = data?.users || [];

  return (
    <div className="space-y-6">
      <div className="flex justify-between items-center">
        <div>
          <h1 className="text-2xl font-bold text-gray-900">User Management</h1>
          <p className="text-gray-500">{users.length} users</p>
        </div>
        <Link to="/admin">
          <Button variant="secondary">Back to Admin</Button>
        </Link>
      </div>

      {error && <Alert variant="error">{error}</Alert>}
      {success && <Alert variant="success">{success}</Alert>}

      <div className="bg-white shadow rounded-lg overflow-hidden">
        <table className="min-w-full divide-y divide-gray-200">
          <thead className="bg-gray-50">
            <tr>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                User
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                Status
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                Role
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                Projects
              </th>
              <th className="px-6 py-3 text-left text-xs font-medium text-gray-500 uppercase">
                Created
              </th>
              <th className="px-6 py-3 text-right text-xs font-medium text-gray-500 uppercase">
                Actions
              </th>
            </tr>
          </thead>
          <tbody className="bg-white divide-y divide-gray-200">
            {users.map((user) => {
              const isCurrentUser = user.id === currentUser?.id;

              return (
                <tr key={user.id} className="hover:bg-gray-50">
                  <td className="px-6 py-4 whitespace-nowrap">
                    <div>
                      <div className="text-sm font-medium text-gray-900">
                        {user.displayName || user.email}
                      </div>
                      <div className="text-sm text-gray-500">{user.email}</div>
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <div className="flex flex-col space-y-1">
                      {user.isLockedOut && (
                        <Badge variant="error">Locked</Badge>
                      )}
                      {!user.emailVerified && (
                        <Badge variant="warning">Unverified</Badge>
                      )}
                      {!user.isLockedOut && user.emailVerified && (
                        <Badge variant="success">Active</Badge>
                      )}
                    </div>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap">
                    <Badge variant={user.isAdmin ? 'primary' : 'default'}>
                      {user.isAdmin ? 'Admin' : 'User'}
                    </Badge>
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {user.projectCount}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-sm text-gray-500">
                    {new Date(user.createdAt).toLocaleDateString()}
                  </td>
                  <td className="px-6 py-4 whitespace-nowrap text-right text-sm font-medium space-x-2">
                    {!isCurrentUser && (
                      <>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => impersonateMutation.mutate(user.id)}
                          disabled={impersonateMutation.isPending}
                        >
                          Impersonate
                        </Button>
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => {
                            changeRoleMutation.mutate({
                              userId: user.id,
                              isAdmin: !user.isAdmin,
                            });
                          }}
                          disabled={changeRoleMutation.isPending}
                        >
                          {user.isAdmin ? 'Demote' : 'Promote'}
                        </Button>
                        {user.isLockedOut ? (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => unlockMutation.mutate(user.id)}
                            disabled={unlockMutation.isPending}
                          >
                            Unlock
                          </Button>
                        ) : (
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => lockMutation.mutate(user.id)}
                            disabled={lockMutation.isPending}
                          >
                            Lock
                          </Button>
                        )}
                        <Button
                          variant="ghost"
                          size="sm"
                          onClick={() => {
                            if (confirm(`Delete user "${user.email}"? This cannot be undone.`)) {
                              deleteMutation.mutate(user.id);
                            }
                          }}
                          disabled={deleteMutation.isPending}
                          className="text-error-600 hover:text-error-700"
                        >
                          Delete
                        </Button>
                      </>
                    )}
                    {isCurrentUser && (
                      <span className="text-gray-400 italic">Current user</span>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
    </div>
  );
}
