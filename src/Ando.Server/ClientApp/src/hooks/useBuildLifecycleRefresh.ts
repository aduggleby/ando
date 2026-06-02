// =============================================================================
// hooks/useBuildLifecycleRefresh.ts
//
// Subscribes to build lifecycle SignalR events and invalidates query caches.
// =============================================================================

import { useEffect } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';

export function useBuildLifecycleRefresh(queryKeys: string[]) {
  const queryClient = useQueryClient();

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/build-logs')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const refresh = () => {
      for (const key of queryKeys) {
        queryClient.invalidateQueries({ queryKey: [key] });
      }
    };

    connection.on('BuildQueued', refresh);
    connection.on('BuildStatusChanged', refresh);
    connection.on('BuildCompleted', refresh);

    connection.start().catch(() => {});

    return () => {
      connection.off('BuildQueued', refresh);
      connection.off('BuildStatusChanged', refresh);
      connection.off('BuildCompleted', refresh);
      connection.stop().catch(() => {});
    };
  }, [queryClient, queryKeys.join('|')]);
}
