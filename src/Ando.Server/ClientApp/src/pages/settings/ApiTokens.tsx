// =============================================================================
// pages/settings/ApiTokens.tsx
//
// Personal API token management page.
// =============================================================================

import { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { createApiToken, listApiTokens, revokeApiToken } from '@/api/auth';
import { Alert } from '@/components/ui/Alert';
import { Button } from '@/components/ui/Button';
import { Input } from '@/components/ui/Input';
import { Loading } from '@/components/ui/Loading';

export function ApiTokens() {
  const [name, setName] = useState('');
  const [error, setError] = useState('');
  const [newTokenValue, setNewTokenValue] = useState('');
  const [copied, setCopied] = useState(false);
  const queryClient = useQueryClient();

  const { data, isLoading, error: loadError } = useQuery({
    queryKey: ['api-tokens'],
    queryFn: listApiTokens,
  });

  const createMutation = useMutation({
    mutationFn: () => createApiToken(name.trim()),
    onSuccess: (result) => {
      if (!result.success) {
        setError(result.error || 'Failed to create token');
        return;
      }
      setError('');
      setNewTokenValue(result.value || '');
      setName('');
      queryClient.invalidateQueries({ queryKey: ['api-tokens'] });
    },
    onError: () => {
      setError('Failed to create token');
    },
  });

  const revokeMutation = useMutation({
    mutationFn: (tokenId: number) => revokeApiToken(tokenId),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['api-tokens'] });
    },
  });

  const tokens = data?.tokens || [];

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading API tokens..." />;
  }

  if (loadError) {
    return <Alert variant="error">Failed to load API tokens</Alert>;
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-100">API Tokens</h1>
        <p className="mt-1 text-gray-500 dark:text-slate-400">
          Create personal API tokens for scripts and automation.
        </p>
      </div>

      {error && <Alert variant="error">{error}</Alert>}

      {newTokenValue && (
        <Alert variant="warning" title="Copy this token now">
          <p className="mb-3">This value is only shown once.</p>
          <div className="rounded-md bg-gray-900 text-gray-100 p-3 font-mono text-xs break-all dark:bg-slate-950">
            {newTokenValue}
          </div>
          <div className="mt-3 flex gap-2">
            <Button
              variant="secondary"
              size="sm"
              onClick={async () => {
                await navigator.clipboard.writeText(newTokenValue);
                setCopied(true);
                setTimeout(() => setCopied(false), 1500);
              }}
            >
              {copied ? 'Copied' : 'Copy token'}
            </Button>
            <Button variant="ghost" size="sm" onClick={() => setNewTokenValue('')}>
              Dismiss
            </Button>
          </div>
        </Alert>
      )}

      <div className="bg-white border border-gray-200 rounded-xl p-6 dark:bg-slate-900 dark:border-slate-800">
        <h2 className="text-lg font-medium text-gray-900 dark:text-slate-100 mb-4">Create Token</h2>
        <form
          className="space-y-4"
          onSubmit={(e) => {
            e.preventDefault();
            setError('');
            if (name.trim().length < 2) {
              setError('Token name must be at least 2 characters');
              return;
            }
            createMutation.mutate();
          }}
        >
          <Input
            label="Token Name"
            value={name}
            onChange={(e) => setName(e.target.value)}
            placeholder="CI script"
            required
          />
          <Button type="submit" isLoading={createMutation.isPending}>
            Create Token
          </Button>
        </form>
      </div>

      <div className="bg-white border border-gray-200 rounded-xl dark:bg-slate-900 dark:border-slate-800">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-slate-800">
          <h2 className="text-lg font-medium text-gray-900 dark:text-slate-100">Existing Tokens</h2>
        </div>
        <div className="divide-y divide-gray-100 dark:divide-slate-800">
          {tokens.length === 0 ? (
            <div className="px-4 py-8 text-center text-gray-500 dark:text-slate-400">No API tokens yet.</div>
          ) : (
            tokens.map((token) => {
              const isRevoked = !!token.revokedAtUtc;
              return (
                <div key={token.id} className="px-4 py-4 flex items-center justify-between gap-4">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-gray-900 dark:text-slate-100">{token.name}</p>
                    <p className="text-xs text-gray-500 dark:text-slate-400 font-mono">Prefix: {token.prefix}</p>
                    <p className="text-xs text-gray-500 dark:text-slate-400">
                      Created {new Date(token.createdAtUtc).toLocaleString()}
                      {token.lastUsedAtUtc ? ` • Last used ${new Date(token.lastUsedAtUtc).toLocaleString()}` : ' • Never used'}
                    </p>
                  </div>
                  <div className="shrink-0">
                    {isRevoked ? (
                      <span className="text-xs text-gray-500 dark:text-slate-400">Revoked</span>
                    ) : (
                      <Button
                        variant="danger"
                        size="sm"
                        onClick={() => revokeMutation.mutate(token.id)}
                        isLoading={revokeMutation.isPending}
                      >
                        Revoke
                      </Button>
                    )}
                  </div>
                </div>
              );
            })
          )}
        </div>
      </div>
    </div>
  );
}
