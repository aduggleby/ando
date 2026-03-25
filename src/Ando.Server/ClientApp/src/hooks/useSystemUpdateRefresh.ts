// =============================================================================
// hooks/useSystemUpdateRefresh.ts
//
// Subscribes to self-update SignalR events and invalidates update-status queries.
// =============================================================================

import { useEffect } from 'react';
import { HubConnectionBuilder, LogLevel } from '@microsoft/signalr';
import { useQueryClient } from '@tanstack/react-query';

export function useSystemUpdateRefresh(enabled: boolean) {
  const queryClient = useQueryClient();

  useEffect(() => {
    if (!enabled) {
      return;
    }

    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/build-logs')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    const refresh = () => {
      queryClient.invalidateQueries({ queryKey: ['system-update-status'] });
      queryClient.invalidateQueries({ queryKey: ['admin-system-update-status'] });
    };

    connection.on('SystemUpdateStatusChanged', refresh);
    connection.start().catch(() => {});

    return () => {
      connection.off('SystemUpdateStatusChanged', refresh);
      connection.stop().catch(() => {});
    };
  }, [enabled, queryClient]);
}

