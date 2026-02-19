// =============================================================================
// pages/builds/BuildDetails.tsx
//
// Build details page with real-time log streaming via SignalR.
// =============================================================================

import { useState, useEffect, useRef } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { useParams, Link } from 'react-router-dom';
import { HubConnectionBuilder, HubConnection, LogLevel } from '@microsoft/signalr';
import { getBuild, getBuildLogs, cancelBuild, retryBuild } from '@/api/builds';
import { Loading } from '@/components/ui/Loading';
import { Alert } from '@/components/ui/Alert';
import { Badge, getBuildStatusVariant } from '@/components/ui/Badge';
import { Button } from '@/components/ui/Button';
import type { LogEntry } from '@/types';

export function BuildDetails() {
  const { id } = useParams<{ id: string }>();
  const queryClient = useQueryClient();
  const [logs, setLogs] = useState<LogEntry[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [autoScroll, setAutoScroll] = useState(true);
  const logsContainerRef = useRef<HTMLDivElement>(null);
  const connectionRef = useRef<HubConnection | null>(null);

  const { data, isLoading, error } = useQuery({
    queryKey: ['build', id],
    queryFn: () => getBuild(Number(id)),
    enabled: !!id,
    refetchInterval: (query) => {
      // Refetch while build is in progress
      const status = query.state.data?.build?.status;
      return status === 'Running' || status === 'Pending' ? 5000 : false;
    },
  });

  const { data: logsData } = useQuery({
    queryKey: ['build-logs', id],
    queryFn: () => getBuildLogs(Number(id)),
    enabled: !!id,
  });

  // Initialize logs from API
  useEffect(() => {
    if (logsData?.logs) {
      setLogs(logsData.logs);
    }
  }, [logsData]);

  // SignalR connection for real-time logs
  useEffect(() => {
    const build = data?.build;
    if (!build || (build.status !== 'Running' && build.status !== 'Pending')) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/build-log')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connectionRef.current = connection;

    connection.on('ReceiveLog', (logEntry: LogEntry) => {
      setLogs((prev) => [...prev, logEntry]);
    });

    connection.on('BuildCompleted', () => {
      setIsStreaming(false);
      queryClient.invalidateQueries({ queryKey: ['build', id] });
    });

    connection
      .start()
      .then(() => {
        setIsStreaming(true);
        return connection.invoke('JoinBuild', Number(id));
      })
      .catch((err) => {
        console.error('SignalR connection error:', err);
      });

    return () => {
      connection.stop();
      connectionRef.current = null;
    };
  }, [data?.build?.status, id, queryClient]);

  // Auto-scroll logs
  useEffect(() => {
    if (autoScroll && logsContainerRef.current) {
      logsContainerRef.current.scrollTop = logsContainerRef.current.scrollHeight;
    }
  }, [logs, autoScroll]);

  const cancelMutation = useMutation({
    mutationFn: () => cancelBuild(Number(id)),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['build', id] });
    },
  });

  const retryMutation = useMutation({
    mutationFn: () => retryBuild(Number(id)),
    onSuccess: (result) => {
      if (result.buildId) {
        window.location.href = `/builds/${result.buildId}`;
      }
    },
  });

  if (isLoading) {
    return <Loading size="lg" className="py-12" text="Loading build..." />;
  }

  if (error) {
    return <Alert variant="error">Failed to load build</Alert>;
  }

  const build = data?.build;

  if (!build) {
    return <Alert variant="error">Build not found</Alert>;
  }

  const isInProgress = build.status === 'Running' || build.status === 'Pending';

  return (
    <div className="space-y-6">
      {/* Header */}
      <div className="flex justify-between items-start">
        <div>
          <div className="flex items-center space-x-3">
            <h1 className="text-2xl font-bold text-gray-900 dark:text-slate-50">Build #{build.id}</h1>
            <Badge variant={getBuildStatusVariant(build.status)} size="lg">
              {build.status}
            </Badge>
          </div>
          <p className="text-gray-500 dark:text-slate-400">
            <Link to={`/projects/${build.projectId}`} className="text-primary-600 hover:underline dark:text-primary-400">
              {build.projectName}
            </Link>
            {' · '}{build.branch} · {build.shortCommitSha}
          </p>
          {build.commitMessage && (
            <p className="text-sm text-gray-600 mt-1 dark:text-slate-300">{build.commitMessage}</p>
          )}
        </div>
        <div className="flex space-x-3">
          {isInProgress && (
            <Button
              variant="danger"
              onClick={() => cancelMutation.mutate()}
              isLoading={cancelMutation.isPending}
            >
              Cancel Build
            </Button>
          )}
          {(build.status === 'Failed' || build.status === 'Cancelled') && (
            <Button
              onClick={() => retryMutation.mutate()}
              isLoading={retryMutation.isPending}
            >
              Retry Build
            </Button>
          )}
        </div>
      </div>

      {/* Build Info */}
      <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
        <InfoCard title="Started" value={build.startedAt ? formatDateTime(build.startedAt) : 'Pending'} />
        <InfoCard title="Finished" value={build.finishedAt ? formatDateTime(build.finishedAt) : 'In progress'} />
        <InfoCard title="Duration" value={build.duration ? formatDuration(build.duration) : 'Running...'} />
        <InfoCard title="Triggered By" value={build.triggeredBy || 'Webhook'} />
      </div>

      {/* Artifacts */}
      {build.artifacts && build.artifacts.length > 0 && (
        <div className="bg-white shadow rounded-lg dark:bg-slate-900">
          <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-slate-700">
            <h2 className="text-lg font-medium text-gray-900 dark:text-slate-50">Artifacts</h2>
          </div>
          <div className="divide-y divide-gray-200 dark:divide-slate-700">
            {build.artifacts.map((artifact) => (
              <div key={artifact.id} className="px-4 py-3 flex items-center justify-between">
                {/*
                  Test fixtures may provide legacy artifact field names (name/sizeBytes).
                  Normalize to avoid blank names and NaN sizes in the UI.
                */}
                {(() => {
                  const artifactLike = artifact as typeof artifact & { name?: string; sizeBytes?: number };
                  const displayName = artifact.fileName || artifactLike.name || `Artifact ${artifact.id}`;
                  const displaySize = typeof artifact.fileSize === 'number'
                    ? artifact.fileSize
                    : (typeof artifactLike.sizeBytes === 'number' ? artifactLike.sizeBytes : 0);

                  return (
                <div>
                      <p className="text-sm font-medium text-gray-900 dark:text-slate-100">{displayName}</p>
                      <p className="text-xs text-gray-500 dark:text-slate-400">{formatFileSize(displaySize)}</p>
                </div>
                  );
                })()}
                <a
                  href={`/api/builds/${build.id}/artifacts/${artifact.id}`}
                  className="text-primary-600 hover:text-primary-500 text-sm font-medium dark:text-primary-400 dark:hover:text-primary-300"
                  download
                >
                  Download
                </a>
              </div>
            ))}
          </div>
        </div>
      )}

      {/* Build Logs */}
      <div className="bg-white shadow rounded-lg dark:bg-slate-900">
        <div className="px-4 py-5 sm:px-6 border-b border-gray-200 dark:border-slate-700 flex justify-between items-center">
          <div className="flex items-center space-x-3">
            <h2 className="text-lg font-medium text-gray-900 dark:text-slate-50">Build Logs</h2>
            {isStreaming && (
              <span className="flex items-center text-sm text-success-600 dark:text-success-400">
                <span className="w-2 h-2 bg-success-500 rounded-full mr-2 animate-pulse"></span>
                Live
              </span>
            )}
          </div>
          <label className="flex items-center text-sm text-gray-600 dark:text-slate-300">
            <input
              type="checkbox"
              checked={autoScroll}
              onChange={(e) => setAutoScroll(e.target.checked)}
              className="mr-2"
            />
            Auto-scroll
          </label>
        </div>
        <div
          ref={logsContainerRef}
          className="bg-gray-900 text-gray-100 font-mono text-sm p-4 overflow-auto dark:bg-slate-950"
          style={{ maxHeight: '600px' }}
        >
          {logs.length === 0 ? (
            <div className="text-gray-500 italic dark:text-slate-500">
              {isInProgress ? 'Waiting for logs...' : 'No logs available'}
            </div>
          ) : (
            logs.map((log) => (
              <div
                key={log.id}
                className={`whitespace-pre-wrap ${getLogColor(log.level)}`}
              >
                <span className="text-gray-500 select-none dark:text-slate-500">
                  [{formatTime(log.timestamp)}]
                </span>{' '}
                {log.message}
              </div>
            ))
          )}
        </div>
      </div>
    </div>
  );
}

function InfoCard({ title, value }: { title: string; value: string }) {
  return (
    <div className="bg-white shadow rounded-lg px-4 py-4 dark:bg-slate-900">
      <dt className="text-sm font-medium text-gray-500 dark:text-slate-400">{title}</dt>
      <dd className="mt-1 text-sm text-gray-900 dark:text-slate-100">{value}</dd>
    </div>
  );
}

function getLogColor(level?: string): string {
  switch (level?.toLowerCase()) {
    case 'error':
      return 'text-error-400';
    case 'warning':
      return 'text-warning-400';
    case 'success':
      return 'text-success-400';
    default:
      return 'text-gray-100';
  }
}

function formatDateTime(dateStr: string): string {
  return new Date(dateStr).toLocaleString();
}

function formatTime(dateStr: string): string {
  return new Date(dateStr).toLocaleTimeString();
}

function formatDuration(duration: string): string {
  const parts = duration.split(':');
  if (parts.length === 3) {
    const hours = parseInt(parts[0]);
    const minutes = parseInt(parts[1]);
    const seconds = parseInt(parts[2].split('.')[0]);

    if (hours > 0) return `${hours}h ${minutes}m ${seconds}s`;
    if (minutes > 0) return `${minutes}m ${seconds}s`;
    return `${seconds}s`;
  }
  return duration;
}

function formatFileSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  if (bytes < 1024 * 1024 * 1024) return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
  return `${(bytes / (1024 * 1024 * 1024)).toFixed(1)} GB`;
}
